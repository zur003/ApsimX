﻿namespace Models.Core
{
    using APSIM.Shared.Utilities;
    using Models.Factorial;
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using System.Linq;

    /// <summary>
    /// Base class for all models
    /// </summary>
    [Serializable]
    [ValidParent(typeof(Folder))]
    [ValidParent(typeof(Replacements))]
    [ValidParent(typeof(Factor))]
    [ValidParent(typeof(CompositeFactor))]
    public class Model : IModel
    {
        [NonSerialized]
        private IModel modelParent;

        /// <summary>
        /// Initializes a new instance of the <see cref="Model" /> class.
        /// </summary>
        public Model()
        {
            this.Name = GetType().Name;
            this.IsHidden = false;
            this.Children = new List<IModel>();
            IncludeInDocumentation = true;
            Enabled = true;
        }

        /// <summary>
        /// Gets or sets the name of the model
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a list of child models.   
        /// </summary>
        public List<IModel> Children { get; set; }

        /// <summary>
        /// Gets or sets the parent of the model.
        /// </summary>
        [JsonIgnore]
        public IModel Parent { get { return modelParent; } set { modelParent = value; } }

        /// <summary>
        /// Gets or sets a value indicating whether a model is hidden from the user.
        /// </summary>
        [JsonIgnore]
        public bool IsHidden { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the graph should be included in the auto-doc documentation.
        /// </summary>
        public bool IncludeInDocumentation { get; set; }

        /// <summary>
        /// A cleanup routine, in which we clear our child list recursively
        /// </summary>
        public void ClearChildLists()
        {
            foreach (Model child in Children)
                child.ClearChildLists();
            Children.Clear();
        }

        /// <summary>
        /// Gets or sets whether the model is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Controls whether the model can be modified.
        /// </summary>
        public bool ReadOnly { get; set; }

        /// <summary>
        /// Full path to the model.
        /// </summary>
        public string FullPath
        {
            get
            {
                string fullPath = "." + Name;
                IModel parent = Parent;
                while (parent != null)
                {
                    fullPath = fullPath.Insert(0, "." + parent.Name);
                    parent = parent.Parent;
                }

                return fullPath;
            }
        }

        /// <summary>
        /// Find a sibling with a given name.
        /// </summary>
        /// <param name="name">Name of the sibling.</param>
        public IModel FindSibling(string name)
        {
            return FindAllSiblings(name).FirstOrDefault();
        }

        /// <summary>
        /// Find a child with a given name.
        /// </summary>
        /// <param name="name">Name of the child.</param>
        public IModel FindChild(string name)
        {
            return FindAllChildren(name).FirstOrDefault();
        }

        /// <summary>
        /// Find a descendant with a given name.
        /// </summary>
        /// <param name="name">Name of the descendant.</param>
        public IModel FindDescendant(string name)
        {
            return FindAllDescendants(name).FirstOrDefault();
        }

        /// <summary>
        /// Find an ancestor with a given name.
        /// </summary>
        /// <param name="name">Name of the ancestor.</param>
        public IModel FindAncestor(string name)
        {
            return FindAllAncestors(name).FirstOrDefault();
        }

        /// <summary>
        /// Find a model in scope with a given name.
        /// </summary>
        /// <param name="name">Name of the model.</param>
        public IModel FindInScope(string name)
        {
            return FindAllInScope(name).FirstOrDefault();
        }

        /// <summary>
        /// Find a sibling of a given type.
        /// </summary>
        /// <typeparam name="T">Type of the sibling.</typeparam>
        public T FindSibling<T>()
        {
            return FindAllSiblings<T>().FirstOrDefault();
        }

        /// <summary>
        /// Find a child with a given type.
        /// </summary>
        /// <typeparam name="T">Type of the child.</typeparam>
        public T FindChild<T>()
        {
            return FindAllChildren<T>().FirstOrDefault();
        }

        /// <summary>
        /// Performs a depth-first search for a descendant of a given type.
        /// </summary>
        /// <typeparam name="T">Type of the descendant.</typeparam>
        public T FindDescendant<T>()
        {
            return FindAllDescendants<T>().FirstOrDefault();
        }

        /// <summary>
        /// Find an ancestor of a given type.
        /// </summary>
        /// <typeparam name="T">Type of the ancestor.</typeparam>
        public T FindAncestor<T>()
        {
            return FindAllAncestors<T>().FirstOrDefault();
        }

        /// <summary>
        /// Find a model of a given type in scope.
        /// </summary>
        /// <typeparam name="T">Type of model to find.</typeparam>
        public T FindInScope<T>()
        {
            return FindAllInScope<T>().FirstOrDefault();
        }

        /// <summary>
        /// Find a sibling with a given type and name.
        /// </summary>
        /// <param name="name">Name of the sibling.</param>
        /// <typeparam name="T">Type of the sibling.</typeparam>
        public T FindSibling<T>(string name)
        {
            return FindAllSiblings<T>(name).FirstOrDefault();
        }

        /// <summary>
        /// Find a child with a given type and name.
        /// </summary>
        /// <param name="name">Name of the child.</param>
        /// <typeparam name="T">Type of the child.</typeparam>
        public T FindChild<T>(string name)
        {
            return FindAllChildren<T>(name).FirstOrDefault();
        }

        /// <summary>
        /// Find a descendant model with a given type and name.
        /// </summary>
        /// <param name="name">Name of the descendant.</param>
        /// <typeparam name="T">Type of the descendant.</typeparam>
        public T FindDescendant<T>(string name)
        {
            return FindAllDescendants<T>(name).FirstOrDefault();
        }

        /// <summary>
        /// Find an ancestor with a given type and name.
        /// </summary>
        /// <param name="name">Name of the ancestor.</param>
        /// <typeparam name="T">Type of the ancestor.</typeparam>
        public T FindAncestor<T>(string name)
        {
            return FindAllAncestors<T>(name).FirstOrDefault();
        }

        /// <summary>
        /// Find a model in scope with a given type and name.
        /// </summary>
        /// <param name="name">Name of the model.</param>
        /// <typeparam name="T">Type of model to find.</typeparam>
        public T FindInScope<T>(string name)
        {
            return FindAllInScope<T>(name).FirstOrDefault();
        }

        /// <summary>
        /// Find all ancestors of the given type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public IEnumerable<T> FindAllAncestors<T>()
        {
            return FindAllAncestors().OfType<T>();
        }

        /// <summary>
        /// Find all descendants of the given type and name.
        /// </summary>
        /// <typeparam name="T">Type of descendants to return.</typeparam>
        public IEnumerable<T> FindAllDescendants<T>()
        {
            return FindAllDescendants().OfType<T>();
        }

        /// <summary>
        /// Find all siblings of the given type.
        /// </summary>
        /// <typeparam name="T">Type of siblings to return.</typeparam>
        public IEnumerable<T> FindAllSiblings<T>()
        {
            return FindAllSiblings().OfType<T>();
        }

        /// <summary>
        /// Find all children of the given type.
        /// </summary>
        /// <typeparam name="T">Type of children to return.</typeparam>
        public IEnumerable<T> FindAllChildren<T>()
        {
            return FindAllChildren().OfType<T>();
        }

        /// <summary>
        /// Find all models of a given type in scope.
        /// </summary>
        /// <typeparam name="T">Type of models to find.</typeparam>
        public IEnumerable<T> FindAllInScope<T>()
        {
            return FindAllInScope().OfType<T>();
        }

        /// <summary>
        /// Find all siblings with the given type and name.
        /// </summary>
        /// <typeparam name="T">Type of siblings to return.</typeparam>
        /// <param name="name">Name of the siblings.</param>
        public IEnumerable<T> FindAllSiblings<T>(string name)
        {
            return FindAllSiblings(name).OfType<T>();
        }

        /// <summary>
        /// Find all children with the given type and name.
        /// </summary>
        /// <typeparam name="T">Type of children to return.</typeparam>
        /// <param name="name">Name of the children.</param>
        public IEnumerable<T> FindAllChildren<T>(string name)
        {
            return FindAllChildren(name).OfType<T>();
        }

        /// <summary>
        /// Find all descendants with the given type and name.
        /// </summary>
        /// <typeparam name="T">Type of descendants to return.</typeparam>
        /// <param name="name">Name of the descendants.</param>
        public IEnumerable<T> FindAllDescendants<T>(string name)
        {
            return FindAllDescendants(name).OfType<T>();
        }

        /// <summary>
        /// Find all ancestors of the given type.
        /// </summary>
        /// <typeparam name="T">Type of ancestors to return.</typeparam>
        /// <param name="name">Name of the ancestors.</param>
        public IEnumerable<T> FindAllAncestors<T>(string name)
        {
            return FindAllAncestors(name).OfType<T>();
        }

        /// <summary>
        /// Find all models of a given type in scope.
        /// </summary>
        /// <typeparam name="T">Type of models to find.</typeparam>
        /// <param name="name">Name of the models.</param>
        public IEnumerable<T> FindAllInScope<T>(string name)
        {
            return FindAllInScope(name).OfType<T>();
        }

        /// <summary>
        /// Find all siblings with a given name.
        /// </summary>
        /// <param name="name">Name of the siblings.</param>
        public IEnumerable<IModel> FindAllSiblings(string name)
        {
            return FindAllSiblings().Where(s => s.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// Find all children with a given name.
        /// </summary>
        /// <param name="name">Name of the children.</param>
        public IEnumerable<IModel> FindAllChildren(string name)
        {
            return FindAllChildren().Where(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// Find all descendants with a given name.
        /// </summary>
        /// <param name="name">Name of the descendants.</param>
        public IEnumerable<IModel> FindAllDescendants(string name)
        {
            return FindAllDescendants().Where(d => d.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// Find all ancestors with a given name.
        /// </summary>
        /// <param name="name">Name of the ancestors.</param>
        public IEnumerable<IModel> FindAllAncestors(string name)
        {
            return FindAllAncestors().Where(a => a.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// Find all model in scope with a given name.
        /// </summary>
        /// <param name="name">Name of the models.</param>
        public IEnumerable<IModel> FindAllInScope(string name)
        {
            return FindAllInScope().Where(m => m.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// Returns all ancestor models.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<IModel> FindAllAncestors()
        {
            IModel parent = Parent;
            while (parent != null)
            {
                yield return parent;
                parent = parent.Parent;
            }
        }

        /// <summary>
        /// Returns all descendant models.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<IModel> FindAllDescendants()
        {
            foreach (IModel child in Children)
            {
                yield return child;

                foreach (IModel descendant in child.FindAllDescendants())
                    yield return descendant;
            }
        }

        /// <summary>
        /// Returns all sibling models.
        /// </summary>
        public IEnumerable<IModel> FindAllSiblings()
        {
            if (Parent == null || Parent.Children == null)
                yield break;

            foreach (IModel sibling in Parent.Children)
                if (sibling != this)
                    yield return sibling;
        }

        /// <summary>
        /// Returns all children models.
        /// </summary>
        public IEnumerable<IModel> FindAllChildren()
        {
            return Children;
        }

        /// <summary>
        /// Returns all models which are in scope.
        /// </summary>
        public IEnumerable<IModel> FindAllInScope()
        {
            Simulation sim = FindAncestor<Simulation>();
            ScopingRules scope = sim?.Scope ?? new ScopingRules();
            foreach (IModel result in scope.FindAll(this))
                yield return result;
        }

        /// <summary>
        /// Called when the model has been newly created in memory whether from 
        /// cloning or deserialisation.
        /// </summary>
        public virtual void OnCreated() { }

        /// <summary>
        /// Called immediately before a simulation has its links resolved and is run.
        /// It provides an opportunity for a simulation to restructure itself 
        /// e.g. add / remove models.
        /// </summary>
        public virtual void OnPreLink() { }

        /// <summary>
        /// Return true iff a model with the given type can be added to the model.
        /// </summary>
        /// <param name="type">The child type.</param>
        public bool IsChildAllowable(Type type)
        {
            // Simulations objects cannot be added to any other models.
            if (typeof(Simulations).IsAssignableFrom(type))
                return false;

            // If it's not an IModel, it's not a valid child.
            if (!typeof(IModel).IsAssignableFrom(type))
                return false;

            // Functions are currently allowable anywhere
            // Note - IFunction does have a [ValidParent(DropAnywhere = true)]
            // attribute, but the GetAttributes method doesn't look in interfaces.
            if (type.GetInterface("IFunction") != null)
                return true;

            // Is allowable if one of the valid parents of this type (t) matches the parent type.
            foreach (ValidParentAttribute validParent in ReflectionUtilities.GetAttributes(type, typeof(ValidParentAttribute), true))
            {
                if (validParent != null)
                {
                    if (validParent.DropAnywhere)
                        return true;

                    if (validParent.ParentType != null && validParent.ParentType.IsAssignableFrom(GetType()))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get the underlying variable object for the given path.
        /// Note that this can be a variable/property or a model.
        /// Returns null if not found.
        /// </summary>
        /// <param name="path">The path of the variable/model.</param>
        /// <param name="ignoreCase">Perform a case-insensitive search?</param>
        /// <remarks>
        /// See <see cref="Locater"/> for more info about paths.
        /// </remarks>
        public IVariable FindByPath(string path, bool ignoreCase = false)
        {
            return Locator().GetInternal(path, this, ignoreCase);
        }

        /// <summary>
        /// Parent all descendant models.
        /// </summary>
        public void ParentAllDescendants()
        {
            foreach (IModel child in Children)
            {
                child.Parent = this;
                child.ParentAllDescendants();
            }
        }

        /// <summary>
        /// Gets the locater model.
        /// </summary>
        /// <remarks>
        /// This is overriden in class Simulation.
        /// </remarks>
        protected virtual Locater Locator()
        {
            Simulation sim = FindAncestor<Simulation>();
            if (sim != null)
                return sim.Locater;

            // Simulation can be null if this model is not under a simulation e.g. DataStore.
            return new Locater();
        }
    }
}
