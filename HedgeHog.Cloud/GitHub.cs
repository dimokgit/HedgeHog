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
    public static string MakeStrategyName(string name, int index) { return prefix + name + (index == 0 ? "" : "(" + index + ")"); }
    public static string CleanStrategyName(string name) { return Regex.Replace(name, "^" + prefix+"\\", "", RegexOptions.IgnoreCase); }
    public static string MyApp { get; set; } = "HedgeHog";
    #endregion
    #region Gist Client Factory
    static GitHubClient ClientFactory() {
      var client = new GitHubClient(new ProductHeaderValue(MyApp));
      client.Credentials = new Octokit.Credentials("hedgehogalice", "2Bbbbbbb");
      return client;
    }
    #endregion

    public static async Task<IList<Gist>> GistStrategies() {
      return (from gist in await ClientFactory().Gist.GetAll()
              where gist.Name().ToLower().StartsWith(prefix)
              select gist
              ).ToArray();
    }
    /// <summary>
    /// Fetch gistswith content (Could be expensive)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="map">T map(key,description,content)</param>
    /// <returns><typeparamref name="T"/></returns>
    public static async Task<IEnumerable<T>> GistStrategies<T>(Func<string, string, string,int, T> map) {
      var gists = await Task.WhenAll(from gist in await GistStrategies()
                                     select ClientFactory().Gist.Get(gist.Id));
      return MapStrategies(gists, map);

    }

    public static async Task<IEnumerable<Gist>> GistStrategyFindByName(string name) {
      var tasks = (from gist in await GistStrategies()
                   where gist.Name().ToLower() == MakeStrategyName(name.ToLower(),0)
                   select ClientFactory().Gist.Get(gist.Id));
      return (await Task.WhenAll(tasks));
    }

    #region Add, Update,Delete
    public static async Task<Gist> GistStrategyAddOrUpdate(string name, string description,params string[] content) {
      IEnumerable<Gist> gistId = await GistStrategyFindByName(name);
      return await (gistId.Any()
        ? GistStrategyUpdate(gistId.First().Id, name, description, content)
        : GistStrategyAdd(name, description, content));
    }
    public static async Task<Gist> GistStrategyAdd(string name, string description,params string[] contents) {
      var newGist = new NewGist() { Description = description, Public = true };
      contents.ForEach((content,i) => newGist.Files.Add(MakeStrategyName(name,i), content));
      return await ClientFactory().Gist.Create(newGist);
    }
    public static async Task<Gist> GistStrategyUpdate(string id, string name, string description,params string[] contents) {
      var newGist = new GistUpdate() { Description = description };
      contents.ForEach((content,i) => newGist.Files.Add(MakeStrategyName(name, i), new GistFileUpdate { Content = content }));
      return await ClientFactory().Gist.Edit(id, newGist);
    }
    public static async Task GistStrategyDeleteByName(string name) {
      var tasks = (await GistStrategyFindByName(name))
        .Select(async gist => await ClientFactory().Gist.Delete(gist.Id));
      await Task.WhenAll(tasks);
    }
    #endregion

    #region Extensions
    public static string Name(this Gist gist) {
      return gist.Files.Select(file => file.Key).First();
    }
    public static string Strategy(this Gist gist) {
      return gist.Files.Select(file => CleanStrategyName(file.Key)).First();
    }
    #endregion
    #region Helpers
    private static IEnumerable<T> MapStrategies<T>(Gist[] gists, Func<string, string, string,int, T> map,int count = int.MaxValue) {
      return from gist in gists
             from file in gist.Files.Take(count).Select((file, i) => new { file, i })
             select map(CleanStrategyName(file.file.Key), gist.Description, file.file.Value.Content, file.i);
    }

    private static async Task<string> FetchGistContent(GistFile file) {
      return await new HttpClient().GetStringAsync(file.RawUrl);
    }
    #endregion
  }
}
