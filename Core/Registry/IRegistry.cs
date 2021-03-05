using System;
using System.Linq;
using System.Collections.Generic;

using CKAN.Versioning;
using CKAN.Games;

namespace CKAN
{
    /// <summary>
    /// Methods all registry implementations must provide.
    ///
    /// </summary>
    public interface IRegistry
    {
        IEnumerable<InstalledModule> InstalledModules { get; }
        IEnumerable<string>          InstalledDlls    { get; }
        IDictionary<string, ModuleVersion> InstalledDlc { get; }

        SortedDictionary<string, Repository> Repositories { get; }
        void AddRepository(Repository repo);
        void ClearRepositories();

        int? DownloadCount(string identifier);
        void SetDownloadCounts(Repository repo, SortedDictionary<string, int> counts);

        /// <summary>
        /// Returns a simple array of the latest compatible module for each identifier for
        /// the specified version of KSP.
        /// </summary>
        IEnumerable<CkanModule> CompatibleModules(GameVersionCriteria ksp_version);

        /// <summary>
        /// Get full JSON metadata string for a mod's available versions
        /// </summary>
        /// <param name="identifier">Name of the mod to look up</param>
        /// <returns>
        /// JSON formatted string for all the available versions of the mod
        /// </returns>
        string GetAvailableMetadata(string identifier);

        /// <summary>
        /// Returns the latest available version of a module that satisfies the specified version.
        /// Returns null if there's simply no available version for this system.
        /// If no ksp_version is provided, the latest module for *any* KSP version is returned.
        /// <exception cref="ModuleNotFoundKraken">Throws if asked for a non-existent module.</exception>
        /// </summary>
        CkanModule LatestAvailable(string identifier, GameVersionCriteria ksp_version, RelationshipDescriptor relationship_descriptor = null);

        /// <summary>
        /// Returns the max game version that is compatible with the given mod.
        /// </summary>
        /// <param name="identifier">Name of mod to check</param>
        GameVersion LatestCompatibleKSP(string identifier);

        /// <summary>
        /// Returns all available versions of a module.
        /// <exception cref="ModuleNotFoundKraken">Throws if asked for a non-existent module.</exception>
        /// </summary>
        IEnumerable<CkanModule> AvailableByIdentifier(string identifier);

        void BuildTagIndex(ModuleTagList tags);

        /// <summary>
        /// Returns the latest available version of a module that satisfies the specified version and
        /// optionally a RelationshipDescriptor. Takes into account module 'provides', which may
        /// result in a list of alternatives being provided.
        /// Returns an empty list if nothing is available for our system, which includes if no such module exists.
        /// If no KSP version is provided, the latest module for *any* KSP version is given.
        /// </summary>
        List<CkanModule> LatestAvailableWithProvides(
            string                  identifier,
            GameVersionCriteria      ksp_version,
            RelationshipDescriptor  relationship_descriptor = null,
            IEnumerable<CkanModule> toInstall               = null
        );

        /// <summary>
        /// Checks the sanity of the registry, to ensure that all dependencies are met,
        /// and no mods conflict with each other.
        /// <exception cref="InconsistentKraken">Thrown if a inconsistency is found</exception>
        /// </summary>
        void CheckSanity();

        List<string> GetSanityErrors();

        /// <summary>
        /// Do we what we can to repair/preen the registry.
        /// </summary>
        void Repair();

        /// <summary>
        /// Finds and returns all modules that could not exist without the listed modules installed, including themselves.
        /// </summary>
        IEnumerable<string> FindReverseDependencies(
            IEnumerable<string> modulesToRemove, IEnumerable<CkanModule> modulesToInstall = null
        );

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
        IEnumerable<InstalledModule> FindRemovableAutoInstalled(IEnumerable<InstalledModule> installedModules);

        /// <summary>
        /// Gets the installed version of a mod. Does not check for provided or autodetected mods.
        /// </summary>
        /// <returns>The module or null if not found</returns>
        CkanModule GetInstalledVersion(string identifier);

        /// <summary>
        /// Returns the module which owns this file, or null if not known.
        /// Throws a PathErrorKraken if an absolute path is provided.
        /// </summary>
        string FileOwner(string file);

        /// <summary>
        /// Attempts to find a module with the given identifier and version.
        /// </summary>
        /// <returns>The module if it exists, null otherwise.</returns>
        CkanModule GetModuleByVersion(string identifier, ModuleVersion version);

        /// <summary>
        /// Register the supplied module as having been installed, thereby keeping
        /// track of its metadata and files.
        /// </summary>
        void RegisterModule(CkanModule mod, IEnumerable<string> absolute_files, GameInstance ksp, bool autoInstalled);

        /// <summary>
        /// Deregister a module, which must already have its files removed, thereby
        /// forgetting abouts its metadata and files.
        ///
        /// Throws an InconsistentKraken if not all files have been removed.
        /// </summary>
        void DeregisterModule(GameInstance ksp, string module);

        /// <summary>
        /// Returns a simple array of all incompatible modules for
        /// the specified version of KSP.
        /// </summary>
        IEnumerable<CkanModule> IncompatibleModules(GameVersionCriteria ksp_version);

        void RegisterDlc(string identifier, UnmanagedModuleVersion version);
        void ClearDlc();


        /// <summary>
        /// Registers the given DLL as having been installed. This provides some support
        /// for pre-CKAN modules.
        ///
        /// Does nothing if the DLL is already part of an installed module.
        /// </summary>
        void RegisterDll(GameInstance ksp, string absolute_path);

        /// <summary>
        /// Clears knowledge of all DLLs from the registry.
        /// </summary>
        void ClearDlls();

        /// <summary>
        /// Returns the file path of a DLL.
        /// null if not found.
        /// </summary>
        string DllPath(string identifier);

        /// <summary>
        /// Returns a dictionary of all modules installed, along with their
        /// versions.
        /// This includes DLLs, which will have a version type of `DllVersion`.
        /// This includes Provides if set, which will have a version of `ProvidesVersion`.
        /// </summary>
        Dictionary<string, ModuleVersion> Installed(bool withProvides = true, bool withDLLs = true);

        /// <summary>
        /// Find installed modules that are not compatible with the given versions
        /// </summary>
        /// <param name="crit">Version criteria against which to check modules</param>
        /// <returns>
        /// Installed modules that are incompatible, if any
        /// </returns>
        IEnumerable<InstalledModule> IncompatibleInstalled(GameVersionCriteria crit);

        /// <summary>
        /// Returns the InstalledModule, or null if it is not installed.
        /// Does *not* look up virtual modules.
        /// </summary>
        InstalledModule InstalledModule(string identifier);

        /// <summary>
        /// Returns the installed version of a given mod.
        ///     If the mod was autodetected (but present), a version of type `DllVersion` is returned.
        ///     If the mod is provided by another mod (ie, virtual) a type of ProvidesVersion is returned.
        /// </summary>
        /// <param name="with_provides">If set to false will not check for provided versions.</param>
        /// <returns>The version of the mod or null if not found</returns>
        ModuleVersion InstalledVersion(string identifier, bool with_provides = true);

        /// <summary>
        /// Check whether the available_modules list is empty
        /// </summary>
        /// <returns>
        /// True if we have at least one available mod, false otherwise.
        /// </returns>
        bool HasAnyAvailable();

        void SetAllAvailable(string repo, IEnumerable<CkanModule> newAvail);

        /// <summary>
        /// Mark a given module as available.
        /// </summary>
        void AddAvailable(string repo, CkanModule module);

        /// <summary>
        /// Remove the given module from the registry of available modules.
        /// Does *nothing* if the module was not present to begin with.
        /// </summary>
        void RemoveAvailable(string identifier, ModuleVersion version);

        /// <summary>
        /// Get a dictionary of all mod versions indexed by their downloads' SHA-1 hash.
        /// Useful for finding the mods for a group of files without repeatedly searching the entire registry.
        /// </summary>
        /// <returns>
        /// dictionary[sha1] = {mod1, mod2, mod3};
        /// </returns>
        Dictionary<string, List<CkanModule>> GetSha1Index();

        /// <summary>
        /// Get a dictionary of all mod versions indexed by their download URLs' hash.
        /// Useful for finding the mods for a group of URLs without repeatedly searching the entire registry.
        /// </summary>
        /// <returns>
        /// dictionary[urlHash] = {mod1, mod2, mod3};
        /// </returns>
        Dictionary<string, List<CkanModule>> GetDownloadHashIndex();
    }

    /// <summary>
    /// Helpers for <see cref="IRegistry"/>
    /// </summary>
    public static class IRegistryHelpers
    {
        /// <summary>
        /// Helper to call <see cref="IRegistry.GetModuleByVersion(string, ModuleVersion)"/>
        /// </summary>
        public static CkanModule GetModuleByVersion(this IRegistry querier, string ident, string version)
        {
            return querier.GetModuleByVersion(ident, new ModuleVersion(version));
        }

        /// <summary>
        ///     Check if a mod is installed (either via CKAN, DLL, or virtually)
        ///     If withProvides is set to false then we skip the check for if the
        ///     mod has been provided (rather than existing as a real mod).
        /// </summary>
        /// <returns><c>true</c>, if installed<c>false</c> otherwise.</returns>
        public static bool IsInstalled(this IRegistry querier, string identifier, bool with_provides = true)
        {
            return querier.InstalledVersion(identifier, with_provides) != null;
        }

        /// <summary>
        ///     Check if a mod is autodetected.
        /// </summary>
        /// <returns><c>true</c>, if autodetected<c>false</c> otherwise.</returns>
        public static bool IsAutodetected(this IRegistry querier, string identifier)
        {
            return querier.IsInstalled(identifier) && querier.InstalledVersion(identifier) is UnmanagedModuleVersion;
        }

        /// <summary>
        /// Is the mod installed and does it have a newer version compatible with version
        /// We can't update AD mods
        /// </summary>
        public static bool HasUpdate(this IRegistry querier, string identifier, GameVersionCriteria version)
        {
            CkanModule newest_version;
            try
            {
                newest_version = querier.LatestAvailable(identifier, version);
            }
            catch (Exception)
            {
                return false;
            }
            if (newest_version == null
                || !querier.IsInstalled(identifier, false)
                || !newest_version.version.IsGreaterThan(querier.InstalledVersion(identifier)))
            {
                return false;
            }
            // All quick checks pass. Now check the relationships.
            try
            {
                RelationshipResolver resolver = new RelationshipResolver(
                    new CkanModule[] { newest_version },
                    new CkanModule[] { querier.InstalledModule(identifier).Module },
                    new RelationshipResolverOptions()
                    {
                        with_recommends = false,
                        without_toomanyprovides_kraken = true,
                    },
                    querier,
                    version
                );
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Generate a string describing the range of game versions
        /// compatible with the given module.
        /// </summary>
        /// <param name="identifier">Mod name to findDependencyShallow</param>
        /// <returns>
        /// String describing range of compatible game versions.
        /// </returns>
        public static string CompatibleGameVersions(this IRegistry registry, IGame game, string identifier)
        {
            List<CkanModule> releases = registry.AvailableByIdentifier(identifier).ToList();
            if (releases != null && releases.Count > 0) {
                ModuleVersion minMod = null, maxMod = null;
                GameVersion   minKsp = null, maxKsp = null;
                IRegistryHelpers.GetMinMaxVersions(releases, out minMod, out maxMod, out minKsp, out maxKsp);
                return GameVersionRange.VersionSpan(game, minKsp, maxKsp);
            }
            return "";
        }

        /// <summary>
        /// Generate a string describing the range of game versions
        /// compatible with the given module.
        /// </summary>
        /// <param name="identifier">Mod name to findDependencyShallow</param>
        /// <returns>
        /// String describing range of compatible game versions.
        /// </returns>
        public static string CompatibleGameVersions(this IRegistry registry, IGame game, CkanModule module)
        {
            ModuleVersion minMod = null, maxMod = null;
            GameVersion   minKsp = null, maxKsp = null;
            IRegistryHelpers.GetMinMaxVersions(
                new CkanModule[] { module },
                out minMod, out maxMod,
                out minKsp, out maxKsp
            );
            return GameVersionRange.VersionSpan(game, minKsp, maxKsp);
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
            out GameVersion    minKsp, out GameVersion    maxKsp)
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
        /// Is the mod installed and does it have a replaced_by relationship with a compatible version
        /// Check latest information on installed version of mod "identifier" and if it has a "replaced_by"
        /// value, check if there is a compatible version of the linked mod
        /// Given a mod identifier, return a ModuleReplacement containing the relevant replacement
        /// if compatibility matches.
        /// </summary>
        public static ModuleReplacement GetReplacement(this IRegistry querier, string identifier, GameVersionCriteria version)
        {
            // We only care about the installed version
            CkanModule installedVersion;
            try
            {
                installedVersion = querier.GetInstalledVersion(identifier);
            }
            catch (ModuleNotFoundKraken)
            {
                return null;
            }
            return querier.GetReplacement(installedVersion, version);
        }

        public static ModuleReplacement GetReplacement(this IRegistry querier, CkanModule installedVersion, GameVersionCriteria version)
        {
            // Mod is not installed, so we don't care about replacements
            if (installedVersion == null)
                return null;
            // No replaced_by relationship
            if (installedVersion.replaced_by == null)
                return null;

            // Get the identifier from the replaced_by relationship, if it exists
            ModuleRelationshipDescriptor replacedBy = installedVersion.replaced_by;

            // Now we need to see if there is a compatible version of the replacement
            try
            {
                ModuleReplacement replacement = new ModuleReplacement();
                replacement.ToReplace = installedVersion;
                if (installedVersion.replaced_by.version != null)
                {
                    replacement.ReplaceWith = querier.GetModuleByVersion(installedVersion.replaced_by.name, installedVersion.replaced_by.version);
                    if (replacement.ReplaceWith != null)
                    {
                        if (replacement.ReplaceWith.IsCompatibleKSP(version))
                        {
                            return replacement;
                        }
                    }
                }
                else
                {
                    replacement.ReplaceWith = querier.LatestAvailable(installedVersion.replaced_by.name, version);
                    if (replacement.ReplaceWith != null)
                    {
                        if (installedVersion.replaced_by.min_version != null)
                        {
                            if (!replacement.ReplaceWith.version.IsLessThan(replacedBy.min_version))
                            {
                                return replacement;
                            }
                        }
                        else return replacement;
                    }
                }
                return null;
            }
            catch (ModuleNotFoundKraken)
            {
                return null;
            }
        }
    }
}
