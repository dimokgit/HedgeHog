using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Octokit;

namespace HedgeHog.Cloud {
  public static class GitHub {
    #region Properties
    static readonly string prefix = "strategy\\";
    static readonly string prefix_ = "strategy--";
    public static string MakeStrategyName(string name, int index) { return MakeStrategyName(prefix, name, index); }
    public static string MakeStrategyNameArchived(string name, int index) { return MakeStrategyName(prefix_, name, index); }

    private static string MakeStrategyName(string prefix, string name, int index) {
      return prefix + name + (index == 0 ? "" : "(" + index + ")");
    }

    public static string CleanStrategyName(string name) { return Regex.Replace(name, "^" + Regex.Escape(prefix), "", RegexOptions.IgnoreCase); }
    public static string MyApp { get; set; } = "HedgeHog";
    #endregion
    #region Gist Client Factory
    public static GitHubClient ClientFactoryGit() => ClientFactory("dimokgit", "BudG0tov");
    public static GitHubClient ClientFactory() => ClientFactory("dimokgit2", "!Qazxsw2");
    public static GitHubClient ClientFactory(string user, string password) =>
      new GitHubClient(new ProductHeaderValue(MyApp)) { Credentials = new Octokit.Credentials(user, password) };
    #endregion

    #region GitHub
    public static async Task SaveInstruments() {
      var ghClient = Cloud.GitHub.ClientFactoryGit();
      var owner = "dimokgit";
      var repo = "HedgeHog";
      var branch = "master";
      var targetFile = "HedgeHog.Alice.Client/Settings/Instruments.json";
      try {
        // try to get the file (and with the file the last commit sha)
        var existingFile = (await ghClient.Repository.Content.GetAllContentsByRef(owner, repo, targetFile, branch)).First();
        // update the file
        var updateChangeSet = await ghClient.Repository.Content.UpdateFile(owner, repo, targetFile,
           new UpdateFileRequest("Instruments Update", existingFile.Content, existingFile.Sha, branch));
      } catch(Octokit.NotFoundException exc) {
        throw new Exception(new { repo, branch, targetFile } + "", exc);
      }
    }
    #endregion

    public static async Task<IList<Gist>> GistStrategies(bool archived = false) {
      var gists = await (ClientFactory().Gist.GetAll());
      return (from gist in gists
              from name in gist.Names()
              where name.ToLower().ToLower().StartsWith(archived ? prefix_ : prefix)
              select gist
              ).ToArray();
    }
    /// <summary>
    /// Fetch gistswith content (Could be expensive)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="map">T map(key,description,content)</param>
    /// <returns><typeparamref name="T"/></returns>
    public static async Task<IEnumerable<IEnumerable<T>>> GistStrategies<T>(Func<string, string, string, Uri, T> map) {
      var gists = await Task.WhenAll(from gist in await GistStrategies()
                                     select ClientFactory().Gist.Get(gist.Id));
      return MapStrategies(gists, map);

    }

    public static async Task<IEnumerable<Gist>> GistStrategyFindByName(string name, bool archived = false) {
      var tasks = (from gist in await GistStrategies(archived)
                   from gn in gist.Names()
                   where gn.ToLower() == (archived ? (Func<string, int, string>)MakeStrategyNameArchived : MakeStrategyName)(name.ToLower(), 0)
                   select ClientFactory().Gist.Get(gist.Id));
      return (await Task.WhenAll(tasks));
    }

    #region Add, Update,Delete
    public static async Task<Gist> GistStrategyAddOrUpdate(string name, string description, params string[] content) {
      IEnumerable<Gist> gistId = await GistStrategyFindByName(name);
      return await (gistId.Any()
        ? GistStrategyUpdate(gistId.First().Id, name, description, content)
        : GistStrategyAdd(name, description, content));
    }
    public static async Task<Gist> GistStrategyAdd(string name, string description, params string[] contents) {
      var newGist = new NewGist() { Description = description, Public = true };
      contents.ForEach((content, i) => newGist.Files.Add(MakeStrategyName(name, i), content));
      return await ClientFactory().Gist.Create(newGist);
    }
    public static async Task<Gist> GistStrategyUpdate(string id, string name, string description, params string[] contents) {
      var newGist = new GistUpdate() { Description = description };
      contents.ForEach((content, i) => newGist.Files.Add(MakeStrategyName(name, i), new GistFileUpdate { Content = content }));
      return await ClientFactory().Gist.Edit(id, newGist);
    }
    public static async Task<Gist> GistStrategyArchive(string name) {
      Gist gist = (await GistStrategyFindByName(name)).SingleOrDefault();
      if(gist == null)
        throw new Exception(new { gist = name, message = "Not found" } + "");
      var newGist = new GistUpdate();
      gist.Files.ForEach((file, i) => newGist.Files.Add(file.Key, new GistFileUpdate { NewFileName = file.Key.Replace(prefix, prefix_), Content = file.Value.Content }));
      return await ClientFactory().Gist.Edit(gist.Id, newGist);
    }
    public static async Task<Gist> GistStrategyRestoreByName(string name) {
      Gist gist = (await GistStrategyFindByName(name, true)).SingleOrDefault();
      if(gist == null)
        throw new Exception(new { gistArchive = name, message = "Not found" } + "");
      var newGist = new GistUpdate();
      gist.Files.ForEach((file, i) => newGist.Files.Add(file.Key, new GistFileUpdate { NewFileName = file.Key.Replace(prefix_, prefix), Content = file.Value.Content }));
      return await ClientFactory().Gist.Edit(gist.Id, newGist);
    }
    public static async Task GistStrategyDeleteByName(string name, bool permanent, bool archived = false) {
      if(permanent) {
        var tasks = (await GistStrategyFindByName(name, archived))
          .Select(async gist => await ClientFactory().Gist.Delete(gist.Id));
        await Task.WhenAll(tasks);
      } else
        await GistStrategyArchive(name);
    }
    #endregion

    #region Extensions
    public static string[] Names(this Gist gist) {
      return gist.Files.Select(file => file.Key).Take(1).ToArray();
    }
    public static string Strategy(this Gist gist) {
      return gist.Files.Select(file => CleanStrategyName(file.Key)).First();
    }
    #endregion
    #region Helpers
    private static IEnumerable<T> MapStrategies<T>(Gist[] gists, Func<string, string, string, Uri, int, T> map, int count = int.MaxValue) {
      return from gist in gists
             from file in gist.Files.Take(count).Select((file, i) => new { file, i })
             select map(CleanStrategyName(file.file.Key), gist.Description, file.file.Value.Content, new Uri(gist.HtmlUrl), file.i);
    }
    private static IEnumerable<IEnumerable<T>> MapStrategies<T>(Gist[] gists, Func<string, string, string, Uri, T> map) {
      return from gist in gists
             select gist.Files.Select(file => map(CleanStrategyName(file.Key), gist.Description, file.Value.Content, new Uri(gist.HtmlUrl)));
    }

    private static async Task<string> FetchGistContent(GistFile file) {
      return await new HttpClient().GetStringAsync(file.RawUrl);
    }
    #endregion
  }
}
