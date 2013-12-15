namespace Hardcodet.Util.Dependencies
{
  /// <summary>
  /// Simple interface that provides untyped access
  /// to a changed node's core data. 
  /// </summary>
  public interface IDependencyChangeInfo
  {
    /// <summary>
    /// Indicates what kind of change happened in the dependency
    /// chain.
    /// </summary>
    DependencyChangeSource Reason { get; }

    /// <summary>
    /// The name of the member (usually a property) that was changed.
    /// </summary>
    string ChangedMemberName { get; }

    /// <summary>
    /// Gets the <see cref="DependencyNode{T}.LeafValue"/> in case of
    /// an intact dependency chain (as indicated by the
    /// <see cref="DependencyNode{T}.IsChainBroken"/> property, or a
    /// given default value if the chain is broken.
    /// </summary>
    /// <returns>The leaf value of the dependency chain in case of an
    /// intact chain, or the default value of <typeparamref name="T"/>,
    /// if the chain is broken.</returns>
    T TryGetLeafValue<T>(T defaultValue);
  }
}