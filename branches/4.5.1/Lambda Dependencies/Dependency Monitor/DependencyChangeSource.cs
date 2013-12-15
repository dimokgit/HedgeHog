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


using System.Collections.Specialized;
using System.ComponentModel;

namespace Hardcodet.Util.Dependencies
{
  /// <summary>
  /// Change flags that indicate the source of a change in a given
  /// dependency chain.
  /// </summary>
  public enum DependencyChangeSource
  {
    /// <summary>
    /// The targeted value (<see cref="DependencyNode{T}.LeafValue"/>)
    /// was changed.
    /// </summary>
    TargetValueChange = 0,
    /// <summary>
    /// The dependency graph did not change, but the target item implements
    /// <see cref="INotifyPropertyChanged"/> and a property of the targeted
    /// item itself was changed.<br/>
    /// This change type is only possible if
    /// <see cref="DependencyNode{T}.ObserveSubValueChanges"/>
    /// is set to true.
    /// </summary>
    SubValueChanged = 1,
    /// <summary>
    /// The dependency chain was changed, which means that chain's
    /// leaf node now targets to a different reference or value.
    /// </summary>
    DependencyGraphChange = 2,
    /// <summary>
    /// The dependency chain was broken (e.g. because a reference in the
    /// chain was set to null). The targeted value cannot
    /// be resolved.
    /// </summary>
    ChainBroken = 3,
    /// <summary>
    /// If the <see cref="DependencyNode{T}.LeafValue"/> is a collection that
    /// was changed updated. Requires the collection to implement
    /// <see cref="INotifyCollectionChanged"/>.<br/>
    /// This change type is only possible if
    /// <see cref="DependencyNode{T}.ObserveSubValueChanges"/>
    /// is set to true.
    /// </summary>
    TargetCollectionChange = 4
  }
}