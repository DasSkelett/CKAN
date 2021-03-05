using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autofac;
using CKAN.Configuration;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CKAN
{
    /// <summary>
    /// Represents the part of the registry that is specific to a game instance.
    /// Contains references to all the <see cref="LocalRepository"/> active for this instance.
    /// </summary>
    public class InstancePartialRegistry
    {
        [JsonIgnore] private static readonly ILog log = LogManager.GetLogger(typeof (InstancePartialRegistry));

        // [JsonProperty("sorted_repositories")]
        // public SortedDictionary<string, Repository> repositories; // name => Repository

        // name => path
        [JsonProperty]
        public  Dictionary<string, string>          installed_dlls = new Dictionary<string, string>();
        [JsonProperty]
        public  Dictionary<string, InstalledModule> installed_modules = new Dictionary<string, InstalledModule>();
        // filename => module
        [JsonProperty]
        public  Dictionary<string, string>          installed_files = new Dictionary<string, string>();

        [JsonProperty]
        [JsonConverter(typeof(repositoryDictConverter))]
        private Dictionary<string, LocalRepository> localRepositories = new Dictionary<string, LocalRepository>();

        public void AddRepositories(IEnumerable<Repository> repos)
        {
            foreach (var repository in repos)
            {
                localRepositories[repository.name] = new LocalRepository(repository);
            }
        }

        public void AddRepository(Repository repo)
        {
            localRepositories[repo.name] = new LocalRepository(repo);
        }

        public LocalRepository GetRepository(string repo)
        {
            return localRepositories[repo];
        }

        public bool TryGetRepository(string name, out LocalRepository repo)
        {
            try
            {
                repo = GetRepository(name);
                return true;
            }
            catch (KeyNotFoundException)
            {
                repo = null;
                return false;
            }
        }

        public IEnumerable<LocalRepository> GetRepositories()
        {
            return localRepositories.Values;
        }

        public void ClearRepositories()
        {
            localRepositories.Clear();
        }

        private class repositoryDictConverter : JsonConverter
            // <Dictionary<string, LocalRepository>>
        {
            // public override void WriteJson(JsonWriter writer, Dictionary<string, LocalRepository> value, JsonSerializer serializer)
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Dictionary<string, LocalRepository> castedValue = value as Dictionary<string, LocalRepository>;
                // writer.WriteValue(castedValue.Keys);
                writer.WriteStartArray();
                foreach (var repoName in castedValue.Keys)
                {
                    writer.WriteValue(repoName);
                }
                writer.WriteEndArray();
                // TODO move out of here, serializing one file during another one's serialization sounds bad.
                foreach (var repo in castedValue.Values)
                {
                    string path = repo.LocalFile();
                    string directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                    File.WriteAllText(path, Serialize(repo));
                }
            }

            public override bool CanConvert(Type objectType)
            {
                return true;
            }

            private static string Serialize(LocalRepository repo)
            {
                StringBuilder sb = new StringBuilder();
                StringWriter sw = new StringWriter(sb);

                using (JsonTextWriter writer = new JsonTextWriter(sw))
                {
                    writer.Formatting = Formatting.Indented;
                    writer.Indentation = 0;

                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(writer, repo);
                }

                return sw + Environment.NewLine;
            }


            // public override Dictionary<string, LocalRepository> ReadJson(
            //     JsonReader reader, Type objectType, Dictionary<string, LocalRepository> existingValue, bool hasExistingValue, JsonSerializer serializer
            public override object ReadJson(
                JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer
            )
            {
                var settings = new JsonSerializerSettings{
                    Context = serializer.Context
                };

                if (reader.TokenType != JsonToken.Null)
                {
                    if (reader.TokenType == JsonToken.StartArray)
                    {
                        JToken token = JToken.Load(reader);
                        List<string> repoNameList = token.ToObject<List<string>>();

                        var localReposDict = new Dictionary<string, LocalRepository>();

                        foreach (string repoName in repoNameList)
                        {
                            if (File.Exists(LocalRepository.LocalFileOf(repoName)))
                            {
                                localReposDict.Add(repoName,
                                    JsonConvert.DeserializeObject<LocalRepository>(
                                        File.ReadAllText(LocalRepository.LocalFileOf(repoName)), settings
                                    )
                                );
                            }
                            else
                            {
                                // Somehow the local file got missing.
                                // Let's create a new repo and hope it gets refresh from source soon.
                                localReposDict.Add(repoName, new LocalRepository(new Repository(
                                    repoName,
                                    ServiceLocator.Container.Resolve<IConfiguration>().Repositories[repoName]
                                )));
                            }
                        }
                        return localReposDict;
                    }
                }
                return new Dictionary<string, LocalRepository>();
            }
        }
    }
}
