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
using System.Reflection;
using System.Text;
using Hardcodet.Util.Dependencies.WeakEvents;

namespace Hardcodet.Util.Dependencies
{

  /// <summary>
  /// Abstract base class for concrete dependency nodes
  /// (<see cref="DependencyNode{T}"/>). Provides
  /// builder methods and can be used to store nodes of different
  /// types (e.g. DependencyNode{int} vs. DependencyNode{string}
  /// in a single collection.
  /// </summary>
  public abstract class DependencyNode : IDisposable
  {
    public abstract void Dispose();

    internal DependencyNode()
    {
    }


    /// <summary>
    /// Creates dependency chain of <see cref="DependencyNode{T}"/> instances for a given
    /// expression.
    /// </summary>
    /// <typeparam name="T">The type of the expression's compiled return value.</typeparam>
    /// <param name="target">The expression that declares the dependency chain from root
    /// to the target item or value.</param>
    /// <returns>The root dependency node, that links to the targeted item.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="target"/>
    /// is a null reference.</exception>
    public static DependencyNode<T> Create<T>(Expression<Func<T>> target)
    {
      return Create(target, null);
    }


    /// <summary>
    /// Creates dependency chain of <see cref="DependencyNode{T}"/> instances for a given
    /// expression.
    /// </summary>
    /// <typeparam name="T">The type of the expression's compiled return value.</typeparam>
    /// <param name="target">The expression that declares the dependency chain from root
    /// to the target item or value.</param>
    /// <param name="changeHandler">An optional event listener that is being invoked if the
    /// target value changes either through direct change, or because the dependency graph
    /// was changed because of a changed intermediary node.</param>
    /// <returns>The root dependency node, that links to the targeted item.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="target"/>
    /// is a null reference.</exception>
    public static DependencyNode<T> Create<T>(Expression<Func<T>> target,
                                              EventHandler<DependencyChangeEventArgs<T>> changeHandler)
    {
      return DependencyBuilder.CreateDependency(target, changeHandler);
    }


    /// <summary>
    /// Creates dependency chain of <see cref="DependencyNode{T}"/> instances for a given
    /// expression.
    /// </summary>
    /// <typeparam name="T">The type of the expression's compiled return value.</typeparam>
    /// <param name="settings">Settings that define the dependency graph, change listeners
    /// and monitoring behaviour.</param>
    /// <returns>The root dependency node, that links to the targeted item.</returns>
    public static DependencyNode<T> Create<T>(DependencyNodeSettings<T> settings)
    {
      return DependencyBuilder.CreateDependency(settings);
    }
  }


  /// <summary>
  /// Represents a single node in a given expression chain
  /// that is observed in order to keep track of a specified
  /// item, which is reflected through the <see cref="LeafNode"/>
  /// of the node trail.
  /// </summary>
  /// <typeparam name="T">The type of the targeted item. This is the
  /// value of the <see cref="LeafNode"/>, which can be retrieved
  /// from every node in the chain through the <see cref="LeafValue"/>
  /// property.</typeparam>
  public class DependencyNode<T> : DependencyNode
  {
    #region fields

    /// <summary>
    /// Holds a weak reference to the item of the dependency
    /// chain that is being represented by this node.
    /// </summary>
    private readonly WeakReference nodeValue = new WeakReference(null, false);

    /// <summary>
    /// Synchronization token.
    /// </summary>
    private readonly object syncRoot = new object();

    /// <summary>
    /// The next child item of the expression tree. Is null
    /// if this node instance is the <see cref="LeafNode"/>
    /// of the dependency chain.
    /// </summary>
    private DependencyNode<T> childNode;

    #endregion

    #region events and listeners

    /// <summary>
    /// The event handler that is submitted to the <see cref="weakCollectionChangeListener"/>.
    /// Needs to be cached or it will go out of scope due to weak references;
    /// </summary>
    private EventHandler<NotifyCollectionChangedEventArgs> collectionChangeHandler;

    /// <summary>
    /// The event handler that is submitted to the <see cref="weakPropertyChangeListener"/>.
    /// Needs to be cached or it will go out of scope due to weak references;
    /// </summary>
    private EventHandler<PropertyChangedEventArgs> propertyChangeHandler;


    /// <summary>
    /// A weak event that is used to track listeners to the
    /// <see cref="DependencyChanged"/> event.
    /// </summary>
    private readonly WeakEvent<EventHandler<DependencyChangeEventArgs<T>>> dependencyChangedEvent =
        new WeakEvent<EventHandler<DependencyChangeEventArgs<T>>>();

    /// <summary>
    /// Fired whenever a change is being detected for this node or
    /// one of its descendants, or if the <see cref="NodeValue"/>
    /// is set manually through the <see cref="SetNodeValue"/> method.
    /// </summary>
    public event EventHandler<DependencyChangeEventArgs<T>> DependencyChanged
    {
      add { dependencyChangedEvent.Add(value); }
      remove { dependencyChangedEvent.Remove(value); }
    }

    /// <summary>
    /// Handles collection change events, if the monitored <see cref="NodeValue"/> implements
    /// <see cref="INotifyCollectionChanged"/>
    /// </summary>
    private WeakEventProxy<NotifyCollectionChangedEventArgs> weakCollectionChangeListener;

    /// <summary>
    /// Handles property change events of the monitored <see cref="NodeValue"/>.
    /// </summary>
    private WeakEventProxy<PropertyChangedEventArgs> weakPropertyChangeListener;

    #endregion

    #region simple properties

    /// <summary>
    /// The member that declares the this item as a child
    /// of its parent.
    /// </summary>
    public MemberInfo ParentMember { get; set; }

    /// <summary>
    /// Checks whether the targeted dependency item (the value of
    /// the <see cref="LeafNode"/> in the dependency chain) should
    /// be observed for changes of the item's data itself. If true,
    /// the <see cref="DependencyChanged"/> event will also fire
    /// if the <see cref="LeafValue"/> implements one of these
    /// interfaces:<br/>
    /// If the item implements <see cref="INotifyPropertyChanged"/>,
    /// <see cref="DependencyChanged"/> will fire if a property change
    /// of the target item is published.<br/>
    /// If the item is a collection that implements
    /// <see cref="INotifyCollectionChanged"/>, <see cref="DependencyChanged"/>
    /// fires for every change of the collection's contents.<br/>
    /// Defaults to true.
    /// </summary>
    public bool ObserveSubValueChanges { get; private set; }

    /// <summary>
    /// The expression was the base to create this node.
    /// </summary>
    public Expression NodeExpression { get; private set; }

    /// <summary>
    /// The parent node of the expression tree, if the current
    /// node is not the <see cref="RootNode"/>.
    /// </summary>
    public DependencyNode<T> ParentNode { get; private set; }


    /// <summary>
    /// The next child item of the expression tree. Is null
    /// if this node instance is the <see cref="LeafNode"/> of
    /// the dependency chain.
    /// Setting this property automatically sets the
    /// <see cref="ParentNode"/> property of the submitted child
    /// to this node instance.
    /// </summary>
    public DependencyNode<T> ChildNode
    {
      get { return childNode; }
      internal set
      {
        childNode = value;
        if (childNode != null) childNode.ParentNode = this;
      }
    }


    /// <summary>
    /// Gets the root node of the observed expression tree.
    /// </summary>
    public DependencyNode<T> RootNode
    {
      get
      {
        DependencyNode<T> node = this;
        while (node.ParentNode != null) node = node.ParentNode;
        return node;
      }
    }


    /// <summary>
    /// Checks whether the dependency chain of the whole graph
    /// (from root to leaf node) is broken.
    /// </summary>
    public bool IsChainBroken
    {
      get
      {
        //leaf node's value is allowed to be null
        DependencyNode<T> node = LeafNode.ParentNode;

        while (node != null)
        {
          if (node.NodeValue == null) return true;
          node = node.ParentNode;
        }

        //we either have only a single leaf, or every
        //node provides a value, linking to a child
        return false;
      }
    }

    /// <summary>
    /// Checks whether the node is the <see cref="LeafNode"/>
    /// of the dependency chain.
    /// </summary>
    public bool IsLeafNode
    {
      get { return ChildNode == null; }
    }


    /// <summary>
    /// Checks whether the node is the <see cref="RootNode"/>
    /// of the dependency chain.
    /// </summary>
    public bool IsRootNode
    {
      get { return ParentNode == null; }
    }

    #endregion

    #region leaf node / value

    /// <summary>
    /// The bottom node of the expression tree, which represents the
    /// item that was registered for observation.
    /// </summary>
    public DependencyNode<T> LeafNode
    {
      get
      {
        DependencyNode<T> node = this;
        while (node.ChildNode != null) node = node.ChildNode;
        return node;
      }
    }


    /// <summary>
    /// Gets the member name of the observed property or field, if applicable.
    /// </summary>
    public string LeafMemberName
    {
      get { return LeafNode.ParentMember == null ? String.Empty : LeafNode.ParentMember.Name; }
    }


    /// <summary>
    /// Gets the current value of the (observed) leaf node.
    /// </summary>
    public T LeafValue
    {
      get { return (T) (LeafNode.NodeValue ?? default(T)); }
    }

    #endregion

    #region NodeValue

    /// <summary>
    /// The instance of the item. Once set, all descendent
    /// items (starting with the <see cref="ChildNode"/> item
    /// are being refreshed accordingly.<br/>
    /// This property can be set manually through the
    /// <see cref="SetNodeValue"/> method for non-observable items
    /// (e.g. fields, or properties that are not covered by
    /// property-change events of their parent).
    /// </summary>
    public object NodeValue
    {
      get { return nodeValue.IsAlive ? nodeValue.Target : null; }
      internal set
      {
        lock (syncRoot)
        {
          //deregister event listener on current value
          if (nodeValue.IsAlive && nodeValue.Target != null) ResetNodeValue(true);

          //register event listener
          nodeValue.Target = value;

          if (value != null)
          {
            //determine child value, if we have a child
            RegisterListener(value);

            //if the local value changed, descendants need to be refreshed
            RefreshChildNodeValue();
          }
        }
      }
    }

    #endregion

    #region construction

    /// <summary>
    /// Creates a new instance of the dependency node.
    /// </summary>
    /// <param name="observeSubValueChanges">Checks whether the
    /// targeted dependency item (the value of the
    /// <see cref="LeafNode"/> in the dependency chain) should
    /// be observed for changes of the item's data itself.</param>
    /// <param name="nodeExpression">The expression that defines this node.</param>
    public DependencyNode(bool observeSubValueChanges, Expression nodeExpression)
    {
      ObserveSubValueChanges = observeSubValueChanges;
      NodeExpression = nodeExpression;
    }

    #endregion

    #region set / reset node value

    /// <summary>
    /// Allows setting the <see cref="NodeValue"/> manually for targeted
    /// objects that do not support change notificiations through the
    /// <see cref="INotifyPropertyChanged"/> interface.
    /// </summary>
    /// <param name="value">The value to be assigned to the <see cref="NodeValue"/>
    /// property.</param>
    /// <param name="fireChangeEvent">Whether to fire the
    /// <see cref="DependencyChanged"/> event after setting the property or not.</param>
    public void SetNodeValue(object value, bool fireChangeEvent)
    {
      NodeValue = value;
      if (fireChangeEvent)
      {
        DependencyChangeSource reason = ResolvePropertyValueChangeReason();
        NotifyNodeChange(this, reason, ParentMember == null ? null : ParentMember.Name);
      }
    }


    /// <summary>
    /// Recursively resets the stored node value by resetting the
    /// <see cref="nodeValue"/> field, and deregisters event listeners.
    /// </summary>
    private void ResetNodeValue(bool recurse)
    {
      lock (syncRoot)
      {
        var npc = NodeValue as INotifyPropertyChanged;
        if (npc != null && weakPropertyChangeListener != null)
        {
          npc.PropertyChanged -= weakPropertyChangeListener.Handler;
        }

        var ncc = NodeValue as INotifyCollectionChanged;
        if (ncc != null && weakCollectionChangeListener != null)
        {
          ncc.CollectionChanged -= weakCollectionChangeListener.Handler;
        }

        //reset local value
        nodeValue.Target = null;

        if (recurse && ChildNode != null)
        {
          ChildNode.ResetNodeValue(true);
        }
      }
    }

    #endregion

    #region event handling

    /// <summary>
    /// Registers a <see cref="PropertyChangedEventHandler"/>
    /// listener on the current <see cref="NodeValue"/>,
    /// if the object implements <see cref="INotifyPropertyChanged"/>.
    /// </summary>
    private void RegisterListener(object newValue)
    {
      //observe leaf collections
      var ncc = newValue as INotifyCollectionChanged;
      if (IsLeafNode && ObserveSubValueChanges && ncc != null)
      {
        if (weakCollectionChangeListener == null)
        {
          collectionChangeHandler = OnCollectionChanged;
          weakCollectionChangeListener = new WeakEventProxy<NotifyCollectionChangedEventArgs>(collectionChangeHandler);
        }

        ncc.CollectionChanged += weakCollectionChangeListener.Handler;

        //return here - ignore property events if its a collection - otherwise
        //we end up with multiple events in case of a collection change (Count Property,
        //indexers etc.)
        return;
      }


      var npc = newValue as INotifyPropertyChanged;
      if (npc != null && (!IsLeafNode || ObserveSubValueChanges))
      {
        if (weakPropertyChangeListener == null)
        {
          propertyChangeHandler = OnPropertyChanged;
          weakPropertyChangeListener = new WeakEventProxy<PropertyChangedEventArgs>(propertyChangeHandler);
        }

        //track property changes for intermediary nodes and leaf nodes, if we
        //handle sub property changes as well
        npc.PropertyChanged += weakPropertyChangeListener.Handler;
      }
    }


    /// <summary>
    /// Invoked if the <see cref="INotifyPropertyChanged"/> event
    /// for an observed *child node* or the leaf value is being fired. Updates the
    /// <see cref="NodeValue"/> of the observed child node in order
    /// to reflect the new property value.
    /// </summary>
    private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      if (!IsLeafNode && ChildNode.ParentMember.Name != e.PropertyName)
      {
        //the changed item is not part of the dependency chain
        return;
      }


      if (IsLeafNode)
      {
        //a (sub-)property on the leaf node changed, the dependency chain is still the same
        NotifyNodeChange(this, DependencyChangeSource.SubValueChanged, e.PropertyName);
        return;
      }


      //refresh the child node
      lock (syncRoot)
      {
        RefreshChildNodeValue();
      }

      if (ChildNode.IsLeafNode)
      {
        //the target item was changed
        NotifyNodeChange(this, DependencyChangeSource.TargetValueChange, e.PropertyName);
      }
      else
      {
        //trigger actions to announce the change of the child node.
        DependencyChangeSource reason = ResolvePropertyValueChangeReason();
        NotifyNodeChange(ChildNode, reason, e.PropertyName);
      }
    }


    /// <summary>
    /// Change event listener which is being invoked if this node is the
    /// <see cref="LeafNode"/>, which represents a collection that implements
    /// <see cref="INotifyCollectionChanged"/>.
    /// </summary>
    private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
      NotifyNodeChange(this, DependencyChangeSource.TargetCollectionChange,
                       ParentMember == null ? null : ParentMember.Name);
    }


    /// <summary>
    /// Resolves the reason for a changed value.
    /// </summary>
    /// <returns>Change source flag.</returns>
    private DependencyChangeSource ResolvePropertyValueChangeReason()
    {
      //trigger events to publish the change of the child node.
      DependencyChangeSource reason;
      if (IsLeafNode)
      {
        reason = DependencyChangeSource.TargetValueChange;
      }
      else
      {
        reason = IsChainBroken
                     ? DependencyChangeSource.ChainBroken
                     : DependencyChangeSource.DependencyGraphChange;
      }

      return reason;
    }


    /// <summary>
    /// Bubbles property change events up the node hierarchy, and
    /// fires the <see cref="DependencyChanged"/> event on every level.
    /// </summary>
    /// <param name="changedNode">The node that was changed.</param>
    /// <param name="reason">The reason for the change event.</param>
    /// <param name="propertyName">The name of the changed or updated property.</param>
    private void NotifyNodeChange(DependencyNode<T> changedNode, DependencyChangeSource reason, string propertyName)
    {
      //raise change event
      dependencyChangedEvent.Raise(this, new DependencyChangeEventArgs<T>(changedNode, reason, propertyName));

      //bubble change
      if (ParentNode != null) ParentNode.NotifyNodeChange(changedNode, reason, propertyName);
    }

    #endregion

    #region read node value

    /// <summary>
    /// Re-evaluates the <see cref="NodeValue"/> of the underlying
    /// <see cref="ChildNode"/> through reflection.
    /// </summary>
    /// <returns>The resolved child node value, if possible. Otherwise
    /// null.</returns>
    /// <exception cref="InvalidOperationException">If the member type
    /// of the child item is neither a field nor a property.</exception>
    private object RefreshChildNodeValue()
    {
      //get the local node value - cannot be null
      object localValue = NodeValue;
      if (ChildNode == null || localValue == null) return null;

      object childNodeValue;

      //we have a changed child - accordingly, we need to wire up event listeners
      //for all descendants - this is invoked through
      switch (ChildNode.ParentMember.MemberType)
      {
        case MemberTypes.Field:
          var fi = (FieldInfo) ChildNode.ParentMember;
          childNodeValue = fi.GetValue(localValue);
          break;
        case MemberTypes.Property:
          var pi = (PropertyInfo) ChildNode.ParentMember;
          var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
          childNodeValue = pi.GetValue(localValue, flags, null, null, null);
          break;
        default:
          string msg = "Unsupported child member type: {0}";
          msg = String.Format(msg, ChildNode.ParentMember.MemberType);
          throw new InvalidOperationException(msg);
      }

      ChildNode.NodeValue = childNodeValue;
      return childNodeValue;
    }

    #endregion

    #region CreateNodeTrail

    /// <summary>
    /// Helper method that creates a breadcrumb-like representation of the
    /// dependency chain starting with this node.
    /// </summary>
    /// <returns>Breadcrumb trail of the node and its descendants.</returns>
    public string CreateNodeTrail()
    {
      var builder = new StringBuilder();
      DependencyNode<T> node = this;

      while (node != null)
      {
        if (node.NodeValue == null)
        {
          if (node.ParentMember != null) builder.Append(node.ParentMember.ReflectedType.Name);
        }
        else
        {
          builder.Append(String.Format("{0} ({1})", node.NodeValue, node.NodeValue.GetType().Name));
        }

        node = node.ChildNode;

        if (node != null)
        {
          builder.Append("." + node.ParentMember.Name);
          builder.Append(" >> ");
        }
      }

      return builder.ToString();
    }

    #endregion

    #region find child node

    /// <summary>
    /// Tries to find a child node that matches a given expression.
    /// </summary>
    /// <param name="expression">A substring of this node's qualified <see cref="NodeExpression"/>
    /// which serves as the search parameter.</param>
    /// <returns>The node itself or one of its descendants that match the
    /// submitted expression.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="expression"/>
    /// is a null reference.</exception>
    public DependencyNode<T> FindNode<TMember>(Expression<Func<TMember>> expression)
    {
      if (expression == null) throw new ArgumentNullException("expression");
      var body = expression.Body;

      var node = this;
      while (node != null)
      {
        if (String.Equals(node.NodeExpression.ToString(), body.ToString())) return node;
        node = node.ChildNode;
      }

      string msg = "Could not find a descendant expression '{0}'";
      msg = String.Format(msg, expression.ToString());
      throw new ArgumentException(msg, "expression");
    }

    #endregion

    #region finalize / dispose

    /// <summary>
    /// Disposes the object.
    /// </summary>
    /// <remarks>This class is not virtual by design. Derived classes
    /// should override <see cref="Dispose(bool)"/>.
    /// </remarks>
    public override void Dispose()
    {
      Dispose(true);

      // This object will be cleaned up by the Dispose method.
      // Therefore, you should call GC.SupressFinalize to
      // take this object off the finalization queue 
      // and prevent finalization code for this object
      // from executing a second time.
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// This destructor will run only if the <see cref="Dispose()"/>
    /// method does not get called. This gives this base class the
    /// opportunity to finalize.
    /// <para>
    /// Important: Do not provide destructors in types derived from
    /// this class.
    /// </para>
    /// </summary>
    ~DependencyNode()
    {
      //delegate disposal
      Dispose(false);
    }


    /// <summary>
    /// <c>Dispose(bool disposing)</c> executes in two distinct scenarios.
    /// If disposing equals <c>true</c>, the method has been called directly
    /// or indirectly by a user's code. Managed and unmanaged resources
    /// can be disposed.
    /// </summary>
    /// <param name="disposing">If disposing equals <c>false</c>, the method
    /// has been called by the runtime from inside the finalizer and you
    /// should not reference other objects. Only unmanaged resources can
    /// be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
      if (disposing)
      {
        lock (this)
        {
          //clear event listeners
          dependencyChangedEvent.Clear();

          //dispose child node(s)
          if (childNode != null)
          {
            childNode.Dispose();
          }

          //clear local references and event listener
          ResetNodeValue(false);

          if (weakCollectionChangeListener != null) weakCollectionChangeListener.Dispose();
          if (weakPropertyChangeListener != null) weakPropertyChangeListener.Dispose();
        }
      }
    }

    #endregion
  }

}