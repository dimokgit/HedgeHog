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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq.Expressions;

namespace Hardcodet.Util.Dependencies
{
  /// <summary>
  /// Encapsulates settings to be used when creating
  /// a dependency chain through the <see cref="DependencyBuilder"/> 
  /// class.
  /// </summary>
  public class DependencyNodeSettings<T>
  {
    /// <summary>
    /// The expression that declares the dependency chain from root
    /// to target.
    /// </summary>
    public Expression<Func<T>> TargetExpression { get; private set; }

    /// <summary>
    /// An optional event handler, which is registered with the root node's
    /// <see cref="DependencyNode{T}.DependencyChanged"/> event.
    /// </summary>
    public EventHandler<DependencyChangeEventArgs<T>> ChangeHandler { get; set; }

    /// <summary>
    /// Whether to observe properties or collection contents of the
    /// target value, if it implements either <see cref="INotifyPropertyChanged"/>
    /// or <see cref="INotifyCollectionChanged"/>, respectively.
    /// </summary>
    public bool ObserveSubValueChanges { get; set; }


    /// <summary>
    /// Creates a new settings instance.
    /// </summary>
    /// <param name="targetExpression"></param>
    /// <exception cref="ArgumentNullException">If <paramref name="targetExpression"/>
    /// is a null reference.</exception>
    public DependencyNodeSettings(Expression<Func<T>> targetExpression)
    {
      if (targetExpression == null) throw new ArgumentNullException("targetExpression");

      TargetExpression = targetExpression;
      ObserveSubValueChanges = true;
    }
  }
}