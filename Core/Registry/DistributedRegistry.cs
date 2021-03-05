using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using System.Transactions;
using Autofac;
using CKAN.Configuration;
using CKAN.Extensions;
using CKAN.Versioning;
using log4net;
using Newtonsoft.Json;

namespace CKAN
{
    /// <summary>
    /// This is the distributed CKAN registry.
    /// It stores information about installed and available modules.
    /// It interfaces with the <see cref="InstancePartialRegistry"/> and all <see cref="LocalRepository"/> active for
    /// this <see cref="GameInstance"/>.
    /// </summary>
    public class DistributedRegistry : IEnlistmentNotification, IRegistry
    {
        [JsonIgnore] private const int LATEST_REGISTRY_VERSION = 4;
        [JsonIgnore] private static readonly ILog log = LogManager.GetLogger(typeof (DistributedRegistry));

        [JsonProperty] private int registry_version;

        [JsonProperty] private readonly InstancePartialRegistry instancePartialRegistry;

        [JsonIgnore]
        private readonly Dictionary<string, AvailableModule> allAvailableModules
            = new Dictionary<string, AvailableModule>();

        [JsonIgnore]
        public Dictionary<string, HashSet<AvailableModule>> providers
            = new Dictionary<string, HashSet<AvailableModule>>();

        public int? DownloadCount(string identifier)
        {
            foreach (var repo in instancePartialRegistry.GetRepositories())
            {
                // TODO or add them together?
                if (repo.download_counts.TryGetValue(identifier, out int count))
                {
                    return count;
                }
            }
            return null;
        }

        public void SetDownloadCounts(Repository repo, SortedDictionary<string, int> counts)
        {
            if (counts != null)
            {
                var localRepo = instancePartialRegistry.GetRepository(repo.name);
                foreach (var kvp in counts)
                {
                    localRepo.download_counts[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Returns all the activated repositories, sorted by priority and name
        /// </summary>
        [JsonIgnore] public SortedDictionary<string, Repository> Repositories
        {
            get
            {
                return new SortedDictionary<string, Repository>(instancePartialRegistry.GetRepositories().ToDictionary(
                    repo => repo.sourceRepository.name, repo => repo.sourceRepository
                ));
            }
        }

        public void AddRepository(Repository repo)
        {
            // Make sure it's also in the config
            var config = ServiceLocator.Container.Resolve<IConfiguration>();
            if (!config.Repositories.ContainsKey(repo.name))
            {
                config.Repositories.Add(repo.name, repo.uri.ToString());
            }
            instancePartialRegistry.AddRepository(repo);
        }

        public void ClearRepositories()
        {
            instancePartialRegistry.ClearRepositories();
        }

        /// <summary>
        /// Returns all the installed modules
        /// </summary>
        [JsonIgnore] public IEnumerable<InstalledModule> InstalledModules
        {
            get { return instancePartialRegistry.installed_modules.Values; }
        }

        /// <summary>
        /// Returns the names of installed DLLs.
        /// </summary>
        [JsonIgnore] public IEnumerable<string> InstalledDlls
        {
            get { return instancePartialRegistry.installed_dlls.Keys; }
        }

        /// <summary>
        /// Returns the file path of a DLL.
        /// null if not found.
        /// </summary>
        public string DllPath(string identifier)
        {
            return instancePartialRegistry.installed_dlls.TryGetValue(identifier, out string path) ? path : null;
        }

        /// <summary>
        /// A map between module identifiers and versions for official DLC that are installed.
        /// </summary>
        [JsonIgnore] public IDictionary<string, ModuleVersion> InstalledDlc
        {
            get {
                return instancePartialRegistry.installed_modules.Values
                    .Where(im => im.Module.IsDLC)
                    .ToDictionary(im => im.Module.identifier, im => im.Module.version);
            }
        }

        /// <summary>
        /// Find installed modules that are not compatible with the given versions
        /// </summary>
        /// <param name="crit">Version criteria against which to check modules</param>
        /// <returns>
        /// Installed modules that are incompatible, if any
        /// </returns>
        public IEnumerable<InstalledModule> IncompatibleInstalled(GameVersionCriteria crit)
        {
            return instancePartialRegistry.installed_modules.Values
                .Where(im => !im.Module.IsCompatibleKSP(crit));
        }

        #region Registry Upgrades

        [OnDeserialized]
        private void DeSerialisationFixes(StreamingContext context)
        {
            // Our context is our KSP install.
            GameInstance ksp = (GameInstance)context.Context;

            // Older registries didn't have the installed_files list, so we create one
            // if absent.

            if (instancePartialRegistry.installed_files == null)
            {
                log.Warn("Older registry format detected, adding installed files manifest...");
                ReindexInstalled();
            }

            registry_version = LATEST_REGISTRY_VERSION;

            // If the LocalRepository pbjects get filled by deserializing JSON, AddAvailable() doesn't get called,
            // thus allAvailable doesn't get filled
            foreach (var repo in instancePartialRegistry.GetRepositories())
            {
                foreach (var availableModule in repo.available_modules)
                {
                    if (!allAvailableModules.ContainsKey(availableModule.Key))
                    {
                        allAvailableModules[availableModule.Key] = availableModule.Value;
                    }
                    else
                    {
                        allAvailableModules[availableModule.Key].Merge(availableModule.Value);
                    }
                }
            }

            BuildProvidesIndex();
        }

        /// <summary>
        /// Rebuilds our master index of installed_files.
        /// Called on registry format updates, but safe to be triggered at any time.
        /// </summary>
        public void ReindexInstalled()
        {
            instancePartialRegistry.installed_files = new Dictionary<string, string>();

            foreach (InstalledModule module in instancePartialRegistry.installed_modules.Values)
            {
                foreach (string file in module.Files)
                {
                    // Register each file we know about as belonging to the given module.
                    instancePartialRegistry.installed_files[file] = module.identifier;
                }
            }
        }

        /// <summary>
        /// Do we what we can to repair/preen the registry.
        /// </summary>
        public void Repair()
        {
            ReindexInstalled();
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Only to create a new, empty registry for new game instances.
        /// </summary>
        public DistributedRegistry(
            Dictionary<string, InstalledModule>  installed_modules,
            Dictionary<string, string>           installed_dlls,
            Dictionary<string ,Dictionary<string, AvailableModule>>  available_modules_per_repo,
            Dictionary<string, string>           installed_files,
            SortedDictionary<string, Repository> repositories
        )
        {
            // Is there a better way of writing constructors than this? Srsly?
            registry_version       = LATEST_REGISTRY_VERSION;
            instancePartialRegistry = new InstancePartialRegistry();
            instancePartialRegistry.installed_modules = installed_modules;
            instancePartialRegistry.installed_dlls    = installed_dlls;
            instancePartialRegistry.installed_files   = installed_files;
            instancePartialRegistry.AddRepositories(repositories.Values);

            foreach (var kvp in available_modules_per_repo)
            {
                instancePartialRegistry.GetRepository(kvp.Key).available_modules = kvp.Value;

                // Populate the common allAvailableModules.
                // TODO sort by priority so versions of higher prio repos overwrite lower prio ones.
                foreach (var avModuleEntry in kvp.Value)
                {
                    if (allAvailableModules.TryGetValue(avModuleEntry.Key, out AvailableModule existing))
                    {
                        existing.Merge(avModuleEntry.Value);
                    }
                }
            }

            BuildProvidesIndex();
        }


        /// <summary>
        /// To be used only by the deserializer.
        /// If deserializing, we don't want everything put back directly,
        /// thus making sure our version number is preserved, letting us
        /// detect registry version upgrades.
        /// </summary>
        [JsonConstructor]
        private DistributedRegistry()
        { }

        public static DistributedRegistry Empty()
        {
            return new DistributedRegistry(
                new Dictionary<string, InstalledModule>(),
                new Dictionary<string, string>(),
                new Dictionary<string, Dictionary<string, AvailableModule>>(),
                new Dictionary<string, string>(),
                new SortedDictionary<string, Repository>()
            );
        }

        #endregion

        #region Transaction Handling


        // Which transaction we're in
        private string enlisted_tx;

        // JSON serialization of self when enlisted with tx
        private string transaction_backup;

        // Coordinate access of multiple threads to the tx info
        private readonly object txMutex = new object();

        // This *doesn't* get called when we get enlisted in a Tx, it gets
        // called when we're about to commit a transaction. We can *probably*
        // get away with calling .Done() here and skipping the commit phase,
        // but I'm not sure if we'd get InDoubt signalling if we did that.
        public void Prepare(PreparingEnlistment preparingEnlistment)
        {
            log.Debug("Registry prepared to commit transaction");

            preparingEnlistment.Prepared();
        }

        public void InDoubt(Enlistment enlistment)
        {
            // In doubt apparently means we don't know if we've committed or not.
            // Since our TxFileMgr treats this as a rollback, so do we.
            log.Warn("Transaction involving registry in doubt.");
            Rollback(enlistment);
        }

        public void Commit(Enlistment enlistment)
        {
            // Hooray! All Tx participants have signalled they're ready.
            // So we're done, and can clear our resources.

            enlisted_tx = null;
            transaction_backup = null;

            enlistment.Done();
            log.Debug("Registry transaction committed");
        }

        public void Rollback(Enlistment enlistment)
        {
            log.Info("Aborted transaction, rolling back in-memory registry changes.");

            // In theory, this should put everything back the way it was, overwriting whatever
            // we had previously.

            var options = new JsonSerializerSettings {ObjectCreationHandling = ObjectCreationHandling.Replace};

            JsonConvert.PopulateObject(transaction_backup, this, options);

            enlisted_tx = null;
            transaction_backup = null;

            enlistment.Done();
        }

        private void SaveState()
        {
            // Hey, you know what's a great way to back-up your own object?
            // JSON. ;)
            transaction_backup = JsonConvert.SerializeObject(this, Formatting.None);
            log.Debug("State saved");
        }

        /// <summary>
        /// Adds our registry to the current transaction. This should be called whenever we
        /// do anything which may dirty the registry.
        /// </summary>
        private void EnlistWithTransaction()
        {
            // This property is thread static, so other threads can't mess with our value
            if (Transaction.Current != null)
            {
                string current_tx = Transaction.Current.TransactionInformation.LocalIdentifier;

                // Multiple threads might be accessing this shared state, make sure they play nice
                lock (txMutex)
                {
                    if (enlisted_tx == null)
                    {
                        log.DebugFormat("Enlisting registry with tx {0}", current_tx);
                        // Let's save our state before we enlist and potentially allow ourselves
                        // to be reverted by outside code
                        SaveState();
                        Transaction.Current.EnlistVolatile(this, EnlistmentOptions.None);
                        enlisted_tx = current_tx;
                    }
                    else if (enlisted_tx != current_tx)
                    {
                        throw new TransactionalKraken(
                            $"Registry already enlisted with tx {enlisted_tx}, can't enlist with tx {current_tx}");
                    }

                    // If we're here, it's a transaction we're already participating in,
                    // so do nothing.
                }
            }
        }

        #endregion

        public void SetAllAvailable(string repo, IEnumerable<CkanModule> newAvail)
        {
            EnlistWithTransaction();
            // Clear current modules
            instancePartialRegistry.GetRepository(repo).available_modules = new Dictionary<string, AvailableModule>();
            providers.Clear();
            // Add the new modules
            foreach (CkanModule module in newAvail)
            {
                AddAvailable(repo, module);
            }
        }

        /// <summary>
        /// Check whether the available_modules list is empty
        /// </summary>
        /// <returns>
        /// True if we have at least one available mod, false otherwise.
        /// </returns>
        public bool HasAnyAvailable()
        {
            return instancePartialRegistry.GetRepositories().Any(
                repo => repo.available_modules.Count > 0
            );
        }

        /// <summary>
        /// Mark a given module as available.
        /// </summary>
        public void AddAvailable(string repo, CkanModule module)
        {
            EnlistWithTransaction();

            var identifier = module.identifier;
            var repository = instancePartialRegistry.GetRepository(repo);
            // If we've never seen this module before, create an entry for it.
            if (!repository.available_modules.ContainsKey(identifier))
            {
                log.DebugFormat("Adding new available module {0}", identifier);
                repository.available_modules[identifier] = new AvailableModule(identifier);
            }
            if (!allAvailableModules.ContainsKey(identifier))
            {
                log.DebugFormat("Adding new available module {0}", identifier);
                allAvailableModules[identifier] = new AvailableModule(identifier);
            }

            // Now register the actual version that we have.
            // (It's okay to have multiple versions of the same mod.)

            log.DebugFormat("Available: {0} version {1}", identifier, module.version);
            repository.available_modules[identifier].Add(module);
            allAvailableModules[identifier].Add(module);
            BuildProvidesIndexFor(repository.available_modules[identifier]);
            sorter = null;
        }

        /// <summary>
        /// Remove the given module from the registry of available modules.
        /// Does *nothing* if the module was not present to begin with.
        /// </summary>
        public void RemoveAvailable(string identifier, ModuleVersion version)
        {
            EnlistWithTransaction();
            AvailableModule availableModule;
            foreach (var repo in instancePartialRegistry.GetRepositories())
            {
                if (repo.available_modules.TryGetValue(identifier, out availableModule))
                {
                    availableModule.Remove(version);
                }
                if (allAvailableModules.TryGetValue(identifier, out availableModule))
                {
                    availableModule.Remove(version);
                }
            }
        }

        /// <summary>
        /// Removes the given module from the registry of available modules.
        /// Does *nothing* if the module was not present to begin with.</summary>
        public void RemoveAvailable(CkanModule module)
        {
            RemoveAvailable(module.identifier, module.version);
        }

        /// <summary>
        /// <see cref="IRegistry.CompatibleModules"/>
        /// </summary>
        public IEnumerable<CkanModule> CompatibleModules(GameVersionCriteria ksp_version)
        {
            // Set up our compatibility partition
            SetCompatibleVersion(ksp_version);
            return sorter.Compatible.Values.Select(avail => avail.Latest(ksp_version)).ToList();
        }

        /// <summary>
        /// <see cref="IRegistry.IncompatibleModules"/>
        /// </summary>
        public IEnumerable<CkanModule> IncompatibleModules(GameVersionCriteria ksp_version)
        {
            // Set up our compatibility partition
            SetCompatibleVersion(ksp_version);
            return sorter.Incompatible.Values.Select(avail => avail.Latest(null)).ToList();
        }

        /// <summary>
        /// <see cref="IRegistry.LatestAvailable" />
        /// </summary>
        public CkanModule LatestAvailable(
            string module,
            GameVersionCriteria ksp_version,
            RelationshipDescriptor relationship_descriptor = null)
        {
            log.DebugFormat("Finding latest available for {0}", module);

            try
            {
                List<CkanModule> candidates = new List<CkanModule>();
                foreach (var repository in instancePartialRegistry.GetRepositories())
                {
                    if (repository.available_modules.TryGetValue(module, out var availableModule))
                    {
                        var latest = availableModule.Latest(ksp_version, relationship_descriptor);
                        if (latest != null)
                            candidates.Add(latest);
                    }
                }
                return CkanModule.LatestOfList(candidates);
            }
            catch (KeyNotFoundException)
            {
                throw new ModuleNotFoundKraken(module);
            }
        }

        /// <summary>
        /// Find modules with a given identifier
        /// </summary>
        /// <param name="identifier">Identifier of modules to find</param>
        /// <returns>
        /// List of all modules with this identifier
        /// </returns>
        public IEnumerable<CkanModule> AvailableByIdentifier(string identifier)
        {
            log.DebugFormat("Finding all available versions for {0}", identifier);
            List<CkanModule> all = new List<CkanModule>();
            foreach (var repository in instancePartialRegistry.GetRepositories())
            {
                try
                {
                    all.AddRange(repository.available_modules[identifier].AllAvailable());
                }
                catch (KeyNotFoundException)
                { }
            }

            if (!all.Any())
                throw new ModuleNotFoundKraken(identifier);

            return all;
        }

        /// <summary>
        /// Get full JSON metadata string for a mod's available versions
        /// </summary>
        /// <param name="identifier">Name of the mod to look up</param>
        /// <returns>
        /// JSON formatted string for all the available versions of the mod
        /// </returns>
        public string GetAvailableMetadata(string identifier)
        {
            // AvailableModule mergedModule = null;
            // foreach (var repo in instancePartialRegistry.GetRepositories())
            // {
            //     if (repo.available_modules.TryGetValue(identifier, out var am))
            //     {
            //         if (mergedModule == null)
            //         {
            //             mergedModule = am;
            //         }
            //         else
            //         {
            //             mergedModule.Merge(am);
            //         }
            //     }
            // }
            //
            // return mergedModule.FullMetadata();
            return allAvailableModules.TryGetValue(identifier, out var am) ? am.FullMetadata() : null;
        }

        /// <summary>
        /// Return the latest game version compatible with the given mod.
        /// </summary>
        /// <param name="identifier">Name of mod to check</param>
        public GameVersion LatestCompatibleKSP(string identifier)
        {
            return allAvailableModules.TryGetValue(identifier, out var am) ? am.LatestCompatibleKSP() : null;
        }

        /// <summary>
        /// Find the minimum and maximum mod versions and compatible game versions
        /// for a list of modules (presumably different versions of the same mod).
        /// </summary>
        /// <param name="modVersions">The modules to inspect</param>
        /// <param name="minMod">Return parameter for the lowest  mod  version</param>
        /// <param name="maxMod">Return parameter for the highest mod  version</param>
        /// <param name="minKsp">Return parameter for the lowest  game version</param>
        /// <param name="maxKsp">Return parameter for the highest game version</param>
        public static void GetMinMaxVersions(IEnumerable<CkanModule> modVersions,
                out ModuleVersion minMod, out ModuleVersion maxMod,
                out GameVersion   minKsp, out GameVersion   maxKsp)
        {
            minMod = maxMod = null;
            minKsp = maxKsp = null;
            foreach (CkanModule rel in modVersions.Where(v => v != null))
            {
                if (minMod == null || minMod > rel.version)
                {
                    minMod = rel.version;
                }
                if (maxMod == null || maxMod < rel.version)
                {
                    maxMod = rel.version;
                }
                GameVersion relMin = rel.EarliestCompatibleKSP();
                GameVersion relMax = rel.LatestCompatibleKSP();
                if (minKsp == null || !minKsp.IsAny && (minKsp > relMin || relMin.IsAny))
                {
                    minKsp = relMin;
                }
                if (maxKsp == null || !maxKsp.IsAny && (maxKsp < relMax || relMax.IsAny))
                {
                    maxKsp = relMax;
                }
            }
        }

        /// <summary>
        /// Generate the providers index so we can find providing modules quicker
        /// </summary>
        private void BuildProvidesIndex()
        {
            providers.Clear();
            // if (allAvailableModules == null)
            //     return;

            foreach (AvailableModule am in allAvailableModules.Values)
            {
                BuildProvidesIndexFor(am);
            }
        }

        /// <summary>
        /// Ensure one AvailableModule is present in the right spots in the providers index
        /// </summary>
        private void BuildProvidesIndexFor(AvailableModule am)
        {
            foreach (CkanModule m in am.AllAvailable())
            {
                foreach (string provided in m.ProvidesList)
                {
                    if (providers.TryGetValue(provided, out HashSet<AvailableModule> provs))
                        provs.Add(am);
                    else
                        providers.Add(provided, new HashSet<AvailableModule>() { am });
                }
            }
        }

        public void BuildTagIndex(ModuleTagList tags)
        {
            tags.Tags.Clear();
            tags.Untagged.Clear();
            foreach (AvailableModule am in allAvailableModules.Values)
            {
                tags.BuildTagIndexFor(am);
            }
        }

        /// <summary>
        /// <see cref="IRegistry.LatestAvailableWithProvides" />
        /// </summary>
        public List<CkanModule> LatestAvailableWithProvides(
            string                  identifier,
            GameVersionCriteria      ksp_version,
            RelationshipDescriptor  relationship_descriptor = null,
            IEnumerable<CkanModule> toInstall               = null)
        {
            if (providers.TryGetValue(identifier, out HashSet<AvailableModule> provs))
            {
                // For each AvailableModule, we want the latest one matching our constraints
                return provs
                    .Select(am => am.Latest(
                        ksp_version,
                        relationship_descriptor,
                        InstalledModules.Select(im => im.Module),
                        toInstall
                    ))
                    .Where(m => m?.ProvidesList?.Contains(identifier) ?? false)
                    .ToList();
            }
            else
            {
                // Nothing provides this, return empty list
                return new List<CkanModule>();
            }
        }

        /// <summary>
        /// Returns the specified CkanModule with the version specified,
        /// or null if it does not exist.
        /// <see cref = "IRegistry.GetModuleByVersion" />
        /// </summary>
        public CkanModule GetModuleByVersion(string ident, ModuleVersion version)
        {
            log.DebugFormat("Trying to find {0} version {1}", ident, version);

            if (allAvailableModules.TryGetValue(ident, out var am))
            {
                return am.ByVersion(version);
            }
            return null;
        }

        /// <summary>
        /// Register the supplied module as having been installed, thereby keeping
        /// track of its metadata and files.
        /// </summary>
        public void RegisterModule(CkanModule mod, IEnumerable<string> absolute_files, GameInstance ksp, bool autoInstalled)
        {
            EnlistWithTransaction();

            sorter = null;

            // But we also want to keep track of all its files.
            // We start by checking to see if any files are owned by another mod,
            // if so, we abort with a list of errors.

            var inconsistencies = new List<string>();

            // We always work with relative files, so let's get some!
            IEnumerable<string> relative_files = absolute_files
                .Select(x => ksp.ToRelativeGameDir(x))
                .Memoize();

            // For now, it's always cool if a module wants to register a directory.
            // We have to flip back to absolute paths to actually test this.
            foreach (string file in relative_files.Where(file => !Directory.Exists(ksp.ToAbsoluteGameDir(file))))
            {
                string owner;
                if (instancePartialRegistry.installed_files.TryGetValue(file, out owner))
                {
                    // Woah! Registering an already owned file? Not cool!
                    // (Although if it existed, we should have thrown a kraken well before this.)
                    inconsistencies.Add(string.Format(
                        "{0} wishes to install {1}, but this file is registered to {2}",
                        mod.identifier, file, owner
                    ));
                }
            }

            if (inconsistencies.Count > 0)
            {
                throw new InconsistentKraken(inconsistencies);
            }

            // If everything is fine, then we copy our files across. By not doing this
            // in the loop above, we make sure we don't have a half-registered module
            // when we throw our exceptinon.

            // This *will* result in us overwriting who owns a directory, and that's cool,
            // directories aren't really owned like files are. However because each mod maintains
            // its own list of files, we'll remove directories when the last mod using them
            // is uninstalled.
            foreach (string file in relative_files)
            {
                instancePartialRegistry.installed_files[file] = mod.identifier;
            }

            // Finally, register our module proper.
            var installed = new InstalledModule(ksp, mod, relative_files, autoInstalled);
            instancePartialRegistry.installed_modules.Add(mod.identifier, installed);
        }

        /// <summary>
        /// Deregister a module, which must already have its files removed, thereby
        /// forgetting abouts its metadata and files.
        ///
        /// Throws an InconsistentKraken if not all files have been removed.
        /// </summary>
        public void DeregisterModule(GameInstance ksp, string module)
        {
            EnlistWithTransaction();

            sorter = null;

            var inconsistencies = new List<string>();

            var absolute_files = instancePartialRegistry.installed_modules[module].Files.Select(ksp.ToAbsoluteGameDir);
            // Note, this checks to see if a *file* exists; it doesn't
            // trigger on directories, which we allow to still be present
            // (they may be shared by multiple mods.

            foreach (var absolute_file in absolute_files.Where(File.Exists))
            {
                inconsistencies.Add(string.Format(
                    "{0} is registered to {1} but has not been removed!",
                    absolute_file, module));
            }

            if (inconsistencies.Count > 0)
            {
                // Uh oh, what mess have we got ourselves into now, Inconsistency Kraken?
                throw new InconsistentKraken(inconsistencies);
            }

            // Okay, all the files are gone. Let's clear our metadata.
            foreach (string rel_file in instancePartialRegistry.installed_modules[module].Files)
            {
                instancePartialRegistry.installed_files.Remove(rel_file);
            }

            // Bye bye, module, it's been nice having you visit.
            instancePartialRegistry.installed_modules.Remove(module);
        }

        /// <summary>
        /// Registers the given DLL as having been installed. This provides some support
        /// for pre-CKAN modules.
        ///
        /// Does nothing if the DLL is already part of an installed module.
        /// </summary>
        public void RegisterDll(GameInstance ksp, string absolute_path)
        {
            EnlistWithTransaction();

            string relative_path = ksp.ToRelativeGameDir(absolute_path);

            string owner;
            if (instancePartialRegistry.installed_files.TryGetValue(relative_path, out owner))
            {
                log.InfoFormat(
                    "Not registering {0}, it belongs to {1}",
                    relative_path,
                    owner
                );
                return;
            }

            // http://xkcd.com/208/
            // This regex works great for things like GameData/Foo/Foo-1.2.dll
            Match match = Regex.Match(
                relative_path, @"
                    ^GameData/            # DLLs only live in GameData
                    (?:.*/)?              # Intermediate paths (ending with /)
                    (?<modname>[^.]+)     # Our DLL name, up until the first dot.
                    .*\.dll$              # Everything else, ending in dll
                ",
                RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
            );

            string modName = match.Groups["modname"].Value;

            if (modName.Length == 0)
            {
                log.WarnFormat("Attempted to index {0} which is not a DLL", relative_path);
                return;
            }

            log.InfoFormat("Registering {0} from {1}", modName, relative_path);

            // We're fine if we overwrite an existing key.
            instancePartialRegistry.installed_dlls[modName] = relative_path;
        }

        /// <summary>
        /// Clears knowledge of all DLLs from the registry.
        /// </summary>
        public void ClearDlls()
        {
            EnlistWithTransaction();
            instancePartialRegistry.installed_dlls = new Dictionary<string, string>();
        }

        public void RegisterDlc(string identifier, UnmanagedModuleVersion version)
        {
            CkanModule dlcModule = null;
            if (allAvailableModules.TryGetValue(identifier, out AvailableModule avail))
            {
                dlcModule = avail.ByVersion(version);
            }
            if (dlcModule == null)
            {
                // Don't have the real thing, make a fake one
                dlcModule = new CkanModule()
                {
                    spec_version = new ModuleVersion("v1.28"),
                    identifier   = identifier,
                    name         = identifier,
                    @abstract    = "An official expansion pack for KSP",
                    author       = new List<string>() { "SQUAD" },
                    version      = version,
                    kind         = "dlc",
                    license      = new List<License>() { new License("restricted") },
                };
                dlcModule.CalculateSearchables();
            }
            instancePartialRegistry.installed_modules.Add(
                identifier,
                new InstalledModule(null, dlcModule, new string[] { }, false)
            );
        }

        public void ClearDlc()
        {
            var installedDlcs = instancePartialRegistry.installed_modules.Values
                .Where(instMod => instMod.Module.IsDLC)
                .ToList();
            foreach (var instMod in installedDlcs)
            {
                instancePartialRegistry.installed_modules.Remove(instMod.identifier);
            }
        }

        /// <summary>
        /// <see cref = "IRegistry.Installed" />
        /// </summary>
        public Dictionary<string, ModuleVersion> Installed(bool withProvides = true, bool withDLLs = true)
        {
            var installed = new Dictionary<string, ModuleVersion>();

            if (withDLLs)
            {
                // Index our DLLs, as much as we dislike them.
                foreach (var dllinfo in instancePartialRegistry.installed_dlls)
                {
                    installed[dllinfo.Key] = new UnmanagedModuleVersion(null);
                }
            }

            // Index our provides list, so users can see virtual packages
            if (withProvides)
            {
                foreach (var provided in ProvidedByInstalled())
                {
                    installed[provided.Key] = provided.Value;
                }
            }

            // Index our installed modules (which may overwrite the installed DLLs and provides)
            // (Includes DLCs)
            foreach (var modinfo in instancePartialRegistry.installed_modules)
            {
                installed[modinfo.Key] = modinfo.Value.Module.version;
            }

            return installed;
        }

        /// <summary>
        /// <see cref = "IRegistry.InstalledModule" />
        /// </summary>
        public InstalledModule InstalledModule(string module)
        {
            // In theory, someone could then modify the data they get back from
            // this, so we sea-lion just in case.

            EnlistWithTransaction();

            InstalledModule installedModule;
            return instancePartialRegistry.installed_modules.TryGetValue(module, out installedModule) ? installedModule : null;
        }

        /// <summary>
        /// Find modules provided by currently installed modules
        /// </summary>
        /// <returns>
        /// Dictionary of provided (virtual) modules and a
        /// ProvidesVersion indicating what provides them
        /// </returns>
        internal Dictionary<string, ProvidesModuleVersion> ProvidedByInstalled()
        {
            var installed = new Dictionary<string, ProvidesModuleVersion>();

            foreach (var modinfo in instancePartialRegistry.installed_modules)
            {
                CkanModule module = modinfo.Value.Module;

                // Skip if this module provides nothing.
                if (module.provides == null)
                {
                    continue;
                }

                foreach (string provided in module.provides)
                {
                    installed[provided] = new ProvidesModuleVersion(module.identifier, module.version.ToString());
                }
            }

            return installed;
        }

        /// <summary>
        /// <see cref = "IRegistry.InstalledVersion" />
        /// </summary>
        public ModuleVersion InstalledVersion(string modIdentifier, bool with_provides=true)
        {
            InstalledModule installedModule;

            // If it's genuinely installed, return the details we have.
            // (Includes DLCs)
            if (instancePartialRegistry.installed_modules.TryGetValue(modIdentifier, out installedModule))
            {
                return installedModule.Module.version;
            }

            // If it's in our autodetected registry, return that.
            if (instancePartialRegistry.installed_dlls.ContainsKey(modIdentifier))
            {
                return new UnmanagedModuleVersion(null);
            }

            // Finally we have our provided checks. We'll skip these if
            // withProvides is false.
            if (!with_provides) return null;

            var provided = ProvidedByInstalled();

            ProvidesModuleVersion version;
            return provided.TryGetValue(modIdentifier, out version) ? version : null;
        }

        /// <summary>
        /// <see cref = "IRegistry.GetInstalledVersion" />
        /// </summary>
        public CkanModule GetInstalledVersion(string mod_identifier)
        {
            InstalledModule installedModule;
            return instancePartialRegistry.installed_modules.TryGetValue(mod_identifier, out installedModule)
                ? installedModule.Module
                : null;
        }

        /// <summary>
        /// Returns the module which owns this file, or null if not known.
        /// Throws a PathErrorKraken if an absolute path is provided.
        /// </summary>
        public string FileOwner(string file)
        {
            file = CKANPathUtils.NormalizePath(file);

            if (Path.IsPathRooted(file))
            {
                throw new PathErrorKraken(
                    file,
                    "KSPUtils.FileOwner can only work with relative paths."
                );
            }

            string fileOwner;
            return instancePartialRegistry.installed_files.TryGetValue(file, out fileOwner) ? fileOwner : null;
        }

        /// <summary>
        /// <see cref="IRegistry.CheckSanity"/>
        /// </summary>
        public void CheckSanity()
        {
            IEnumerable<CkanModule> installed = from pair in instancePartialRegistry.installed_modules select pair.Value.Module;
            SanityChecker.EnforceConsistency(installed, instancePartialRegistry.installed_dlls.Keys, InstalledDlc);
        }

        public List<string> GetSanityErrors()
        {
            var installed = from pair in instancePartialRegistry.installed_modules select pair.Value.Module;
            return SanityChecker.ConsistencyErrors(installed, instancePartialRegistry.installed_dlls.Keys, InstalledDlc).ToList();
        }

        /// <summary>
        /// Finds and returns all modules that could not exist without the listed modules installed, including themselves.
        /// Acts recursively and lazily.
        /// </summary>
        /// <param name="modulesToRemove">Modules that are about to be removed.</param>
        /// <param name="modulesToInstall">Optional list of modules that are about to be installed.</param>
        /// <param name="origInstalled">Modules that are already installed</param>
        /// <param name="dlls">Installed DLLs</param>
        /// <param name="dlc">Installed DLCs</param>
        /// <returns>List of modules whose dependencies are about to be or already removed.</returns>
        internal static IEnumerable<string> FindReverseDependencies(
            IEnumerable<string> modulesToRemove,
            IEnumerable<CkanModule> modulesToInstall,
            IEnumerable<CkanModule> origInstalled,
            IEnumerable<string> dlls,
            IDictionary<string, ModuleVersion> dlc
        )
        {
            modulesToRemove = modulesToRemove.Memoize();
            origInstalled    = origInstalled.Memoize();
            var dllSet = dlls.ToHashSet();
            // The empty list has no reverse dependencies
            // (Don't remove broken modules if we're only installing)
            if (modulesToRemove.Any())
            {
                // All modules in the input are included in the output
                foreach (string starter in modulesToRemove)
                {
                    yield return starter;
                }
                while (true)
                {
                    // Make our hypothetical install, and remove the listed modules from it.
                    HashSet<CkanModule> hypothetical = new HashSet<CkanModule>(origInstalled); // Clone because we alter hypothetical.
                    if (modulesToInstall != null)
                    {
                        // Pretend the mods we are going to install are already installed, so that dependencies that will be
                        // satisfied by a mod that is going to be installed count as satisfied.
                        hypothetical = hypothetical.Concat(modulesToInstall).ToHashSet();
                    }
                    hypothetical.RemoveWhere(mod => modulesToRemove.Contains(mod.identifier));

                    log.DebugFormat("Started with {0}, removing {1}, and keeping {2}; our dlls are {3}", string.Join(", ", origInstalled), string.Join(", ", modulesToRemove), string.Join(", ", hypothetical), string.Join(", ", dllSet));

                    // Find what would break with this configuration.
                    var broken = SanityChecker.FindUnsatisfiedDepends(hypothetical, dllSet, dlc)
                        .Select(x => x.Key.identifier).ToHashSet();

                    if (modulesToInstall != null)
                    {
                        // Make sure to only report modules as broken if they are actually currently installed.
                        // This is mainly to remove the modulesToInstall again which we added
                        // earlier to the hypothetical list.
                        broken.IntersectWith(origInstalled.Select(m => m.identifier));
                    }
                    // Lazily return each newly found rev dep
                    foreach (string newFound in broken.Except(modulesToRemove))
                    {
                        yield return newFound;
                    }

                    // If nothing else would break, it's just the list of modules we're removing.
                    HashSet<string> to_remove = new HashSet<string>(modulesToRemove);

                    if (to_remove.IsSupersetOf(broken))
                    {
                        log.DebugFormat("{0} is a superset of {1}, work done", string.Join(", ", to_remove), string.Join(", ", broken));
                        break;
                    }

                    // Otherwise, remove our broken modules as well, and recurse.
                    broken.UnionWith(to_remove);
                    modulesToRemove = broken;
                }
            }
        }

        /// <summary>
        /// Return modules which are dependent on the modules passed in or modules in the return list
        /// </summary>
        public IEnumerable<string> FindReverseDependencies(
            IEnumerable<string> modulesToRemove,
            IEnumerable<CkanModule> modulesToInstall = null
        )
        {
            var installed = new HashSet<CkanModule>(instancePartialRegistry.installed_modules.Values.Select(x => x.Module));
            return FindReverseDependencies(modulesToRemove, modulesToInstall, installed, new HashSet<string>(instancePartialRegistry.installed_dlls.Keys), InstalledDlc);
        }

        /// <summary>
        /// Find auto-installed modules that have no depending modules
        /// or only auto-installed depending modules.
        /// </summary>
        /// <param name="installedModules">The modules currently installed</param>
        /// <param name="dlls">The DLLs that are manually installed</param>
        /// <param name="dlc">The DLCs that are installed</param>
        /// <returns>
        /// Sequence of removable auto-installed modules, if any
        /// </returns>
        private static IEnumerable<InstalledModule> FindRemovableAutoInstalled(
            IEnumerable<InstalledModule>       installedModules,
            IEnumerable<string>                dlls,
            IDictionary<string, ModuleVersion> dlc
        )
        {
            // ToList ensures that the collection isn't modified while the enumeration operation is executing
            installedModules = installedModules.Memoize();
            var autoInstMods = installedModules.Where(im => im.AutoInstalled).ToList();
            var autoInstIds  = autoInstMods.Select(im => im.Module.identifier).ToHashSet();
            var instCkanMods = installedModules.Select(im => im.Module);
            return autoInstMods.Where(
                im => autoInstIds.IsSupersetOf(FindReverseDependencies(
                    new List<string> { im.identifier }, null, instCkanMods, dlls, dlc)));
        }

        /// <summary>
        /// Find auto-installed modules that have no depending modules
        /// or only auto-installed depending modules.
        /// installedModules is a parameter so we can experiment with
        /// changes that have not yet been made, such as removing other modules.
        /// </summary>
        /// <param name="installedModules">The modules currently installed</param>
        /// <returns>
        /// Sequence of removable auto-installed modules, if any
        /// </returns>
        public IEnumerable<InstalledModule> FindRemovableAutoInstalled(IEnumerable<InstalledModule> installedModules)
        {
            return FindRemovableAutoInstalled(installedModules, InstalledDlls, InstalledDlc);
        }

        /// <summary>
        /// Get a dictionary of all mod versions indexed by their downloads' SHA-1 hash.
        /// Useful for finding the mods for a group of files without repeatedly searching the entire registry.
        /// </summary>
        /// <returns>
        /// dictionary[sha1] = {mod1, mod2, mod3};
        /// </returns>
        public Dictionary<string, List<CkanModule>> GetSha1Index()
        {
            var index = new Dictionary<string, List<CkanModule>>();
            foreach (CkanModule mod in allAvailableModules.Values.SelectMany(am => am.module_version.Values))
            {
                if (mod.download_hash != null)
                {
                    if (index.ContainsKey(mod.download_hash.sha1))
                    {
                        index[mod.download_hash.sha1].Add(mod);
                    }
                    else
                    {
                        index.Add(mod.download_hash.sha1, new List<CkanModule>() {mod});
                    }
                }
            }
            return index;
        }

        /// <summary>
        /// Get a dictionary of all mod versions indexed by their download URLs' hash.
        /// Useful for finding the mods for a group of URLs without repeatedly searching the entire registry.
        /// </summary>
        /// <returns>
        /// dictionary[urlHash] = {mod1, mod2, mod3};
        /// </returns>
        public Dictionary<string, List<CkanModule>> GetDownloadHashIndex()
        {
            var index = new Dictionary<string, List<CkanModule>>();
            foreach (CkanModule mod in allAvailableModules.Values.SelectMany(am => am.module_version.Values))
            {
                if (mod.download != null)
                {
                    string hash = NetFileCache.CreateURLHash(mod.download);
                    if (index.ContainsKey(hash))
                    {
                        index[hash].Add(mod);
                    }
                    else
                    {
                        index.Add(hash, new List<CkanModule>() {mod});
                    }
                }
            }
            return index;
        }

        /// <summary>
        /// Partition all CkanModules in available_modules into
        /// compatible and incompatible groups.
        /// </summary>
        /// <param name="versCrit">Version criteria to determine compatibility</param>
        public void SetCompatibleVersion(GameVersionCriteria versCrit)
        {
            if (!versCrit.Equals(sorter?.CompatibleVersions))
            {
                sorter = new CompatibilitySorter(
                    versCrit, allAvailableModules, providers,
                    instancePartialRegistry.installed_modules,
                    InstalledDlls.ToHashSet(), InstalledDlc
                );
            }
        }

        [JsonIgnore] private CompatibilitySorter sorter;
    }
}
