using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Autofac;
using CKAN.Configuration;
using Newtonsoft.Json;

namespace CKAN
{
    /// <summary>
    /// Represents a repository as it's saved on the disk.
    /// Contains all the modules available from this repository, download counts for the modules of this repository,
    /// and further data calculated at load.
    /// Linked to a <see cref="Repository"/> which represents the "online" part of it.
    /// </summary>
    public class LocalRepository
    {
        // TODO make sourceRepository a JsonProperty, to also save eTag, URI and stuff.
        // It's already used my MonolithicRegistry.
        [JsonIgnore]   public Repository sourceRepository { get; private set; }
        [JsonProperty] private readonly string sourceRepositoryName;

        // TODO: These may be good as custom types, especially those which process
        // paths (and flip from absolute to relative, and vice-versa).
        [JsonProperty] internal Dictionary<string, AvailableModule> available_modules = new Dictionary<string, AvailableModule>();
        [JsonProperty] public readonly SortedDictionary<string, int> download_counts = new SortedDictionary<string, int>();


        public string LocalFile()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CKAN",
                "repositories",
                sourceRepository.name + ".json"// TODO make path safe
            );
        }

        public static string LocalFileOf(string name)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CKAN",
                "repositories",
                name + ".json"
            );
        }

        public LocalRepository(Repository repository)
        {
            sourceRepository = repository;
            sourceRepositoryName = sourceRepository.name;
        }

        [JsonConstructor]
        private LocalRepository()
        { }

        [OnDeserialized]
        internal void DeserialisationFixes(StreamingContext context)
        {
            // TODO save URI in JSON
            if (ServiceLocator.Container.Resolve<IConfiguration>()
                .Repositories.TryGetValue(sourceRepositoryName, out var uri))
            {
                sourceRepository = new Repository(sourceRepositoryName, uri);
            }
            else
            {
                throw new Kraken($"Tried to load repository {sourceRepositoryName}, but config.json doesn't know it.");
            }
        }
    }
}
