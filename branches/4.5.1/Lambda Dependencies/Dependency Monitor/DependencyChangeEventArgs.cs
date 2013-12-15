// hardcodet.net Lambda Dependencies
// Copyright (c) 2009 Philipp Sumi
// Contact and Information: http://www.hardcodet.net
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the Code Project Open License (CPOL);
// either version 1.0 of the License, or (at your option) any later
// version.
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
//
// THIS COPYRIGHT NOTICE MAY NOT BE REMOVED FROM THIS FILE.


using System;


namespace Hardcodet.Util.Dependencies
{
  /// <summary>
  /// Event arguments for a dependency's
  /// <see cref="DependencyNode{T}.DependencyChanged"/> event.
  /// </summary>
  public sealed class DependencyChangeEventArgs<T> : EventArgs, IDependencyChangeInfo
  {
    /// <summary>
    /// The node that represents a changed object instance or value,
    /// causing a change of the dependency chain.
    /// </summary>
    public DependencyNode<T> ChangedNode { get; private set; }

    /// <summary>
    /// Indicates what kind of change happened in the dependency
    /// chain.
    /// </summary>
    public DependencyChangeSource Reason { get; private set; }


    /// <summary>
    /// The name of the member (usually a property) that was changed.
    /// </summary>
    public string ChangedMemberName { get; internal set; }



    TLeaf IDependencyChangeInfo.TryGetLeafValue<TLeaf>(TLeaf defaultValue)
    {
      if (ChangedNode.IsChainBroken) return defaultValue;

      //convert the leaf value to the requested type
      return (TLeaf)Convert.ChangeType(ChangedNode.LeafValue, typeof(TLeaf));
    }


    /// <summary>
    /// Gets the <see cref="DependencyNode{T}.LeafValue"/> in case of
    /// an intact dependency chain (as indicated by the
    /// <see cref="DependencyNode{T}.IsChainBroken"/> property, or a
    /// given default value if the chain is broken.
    /// </summary>
    /// <returns>The leaf value of the dependency chain in case of an
    /// intact chain, or the default value of <typeparamref name="T"/>,
    /// if the chain is broken.</returns>
    public T TryGetLeafValue()
    {
      return TryGetLeafValue(default(T));
    }


    /// <summary>
    /// Gets the <see cref="DependencyNode{T}.LeafValue"/> in case of
    /// an intact dependency chain (as indicated by the
    /// <see cref="DependencyNode{T}.IsChainBroken"/> property, or a
    /// given default value if the chain is broken.
    /// </summary>
    /// <param name="defaultValue">The default value to be returned if
    /// the dependency chain is broken.</param>
    /// <returns>The leaf value of the dependency chain in case of an
    /// intact chain, or <paramref name="defaultValue"/>, if the chain
    /// is broken.</returns>
    public T TryGetLeafValue(T defaultValue)
    {
      return ChangedNode.IsChainBroken ? defaultValue : ChangedNode.LeafValue;
    }


    /// <summary>
    /// Creates a new instance of the <see cref="DependencyChangeEventArgs{T}"/>
    /// class.
    /// </summary>
    /// <param name="changedNode">The node that was updated and changed the
    /// dependency chain.</param>
    /// <param name="reason"></param>
    /// <param name="propertyName">The name of the changed property.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="changedNode"/>
    /// is a null reference.</exception>
    public DependencyChangeEventArgs(DependencyNode<T> changedNode, DependencyChangeSource reason, string propertyName)
    {
      if (changedNode == null) throw new ArgumentNullException("changedNode");
      ChangedNode = changedNode;
      Reason = reason;
      ChangedMemberName = propertyName;
    }
  }
}