﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RainmeterStudio.Documents;
using RainmeterStudio.Model;
using RainmeterStudio.Model.Events;
using RainmeterStudio.Utils;

namespace RainmeterStudio.Business
{
    /// <summary>
    /// Document manager
    /// </summary>
    public class DocumentManager
    {
        #region Events

        /// <summary>
        /// Triggered when a document is opened
        /// </summary>
        public event EventHandler<DocumentOpenedEventArgs> DocumentOpened;

        /// <summary>
        /// Triggered when a document is closed
        /// </summary>
        public event EventHandler<DocumentClosedEventArgs> DocumentClosed;

        #endregion

        #region Properties

        /// <summary>
        /// Gets a list of factories
        /// </summary>
        public IEnumerable<IDocumentEditorFactory> Factories { get { return _factories; } }

        /// <summary>
        /// Gets a list of editors
        /// </summary>
        public IEnumerable<IDocumentEditor> Editors { get { return _editors; } }

        /// <summary>
        /// Gets a list of storages
        /// </summary>
        public IEnumerable<IDocumentStorage> Storages { get { return _storages; } }

        #endregion

        #region Private fields

        private List<IDocumentEditorFactory> _factories = new List<IDocumentEditorFactory>();
        private List<IDocumentEditor> _editors = new List<IDocumentEditor>();
        private List<IDocumentStorage> _storages = new List<IDocumentStorage>();
        private List<DocumentTemplate> _templates = new List<DocumentTemplate>();

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes this document manager
        /// </summary>
        public DocumentManager()
        {
        }

        /// <summary>
        /// Registers all classes with the auto register flag
        /// </summary>
        /// <remarks>We love linq</remarks>
        public void PerformAutoRegister()
        {
            // Get all assemblies
            AppDomain.CurrentDomain.GetAssemblies()

            // Get all types
            .SelectMany(assembly => assembly.GetTypes())

            // Select only the classes
            .Where(type => type.IsClass)

            // That have the AutoRegister attribute
            .Where(type => type.GetCustomAttributes(typeof(AutoRegisterAttribute), false).Length > 0)

            // That implement any of the types that can be registered
            .Where((type) =>
            {
                bool res = false;
                res |= typeof(IDocumentEditorFactory).IsAssignableFrom(type);
                res |= typeof(IDocumentStorage).IsAssignableFrom(type);
                res |= typeof(DocumentTemplate).IsAssignableFrom(type);

                return res;
            })

            // Obtain their default constructor
            .Select(type => type.GetConstructor(new Type[0]))

            // Invoke the default constructor
            .Select(constructor => constructor.Invoke(new object[0]))

            // Register
            .ForEach(obj =>
            {
                // Try to register factory
                var factory = obj as IDocumentEditorFactory;
                if (factory != null)
                    RegisterEditorFactory(factory);

                // Try to register as storage
                var storage = obj as IDocumentStorage;
                if (storage != null)
                    RegisterStorage(storage);

                // Try to register as document template
                var doctemplate = obj as DocumentTemplate;
                if (doctemplate != null)
                    RegisterTemplate(doctemplate);
            });
        }

        /// <summary>
        /// Registers a document editor factory
        /// </summary>
        /// <param name="factory">Document editor factory</param>
        public void RegisterEditorFactory(IDocumentEditorFactory factory)
        {
            _factories.Add(factory);
        }

        /// <summary>
        /// Registers a document storage
        /// </summary>
        /// <param name="storage">The storage</param>
        public void RegisterStorage(IDocumentStorage storage)
        {
            _storages.Add(storage);
        }

        /// <summary>
        /// Registers a document template
        /// </summary>
        /// <param name="template">The document template</param>
        public void RegisterTemplate(DocumentTemplate template)
        {
            _templates.Add(template);
        }

        #endregion

        #region Document operations

        /// <summary>
        /// Creates a new document in the specified path, with the specified format, and opens it
        /// </summary>
        /// <param name="format"></param>
        /// <param name="path"></param>
        public IDocumentEditor Create(DocumentTemplate format)
        {
            // Create document
            var document = format.CreateDocument();
            document.IsDirty = true;

            // Find and create editor
            IDocumentEditor editor = CreateEditor(document);

            // Trigger event
            if (DocumentOpened != null)
                DocumentOpened(this, new DocumentOpenedEventArgs(editor));

            return editor;
        }

        /// <summary>
        /// Opens the specified document
        /// </summary>
        /// <param name="path">The path to the file to open</param>
        public IDocumentEditor Open(string path)
        {
            // Try to open
            IDocument document = Read(path);

            // Create factory
            var editor = CreateEditor(document);

            // Trigger event
            if (DocumentOpened != null)
                DocumentOpened(this, new DocumentOpenedEventArgs(editor));

            return editor;
        }

        /// <summary>
        /// Saves a document
        /// </summary>
        /// <param name="document">The document</param>
        public void Save(IDocument document)
        {
            // Find a storage
            var storage = FindStorage(document);

            if (document.Reference == null)
                throw new ArgumentException("Reference cannot be empty");

            // Save
            storage.Write(document.Reference.Path, document);

            // Clear dirty flag
            document.IsDirty = false;
        }

        /// <summary>
        /// Saves the document as
        /// </summary>
        /// <param name="path">Path</param>
        /// <param name="document">Document</param>
        public void SaveAs(string path, IDocument document)
        {
            // Find a storage
            var storage = FindStorage(document);

            // Save
            storage.Write(path, document);

            // Update reference
            document.Reference.Name = Path.GetFileName(path);
            document.Reference.Path = path;

            // Clear dirty flag
            document.IsDirty = false;
        }

        /// <summary>
        /// Saves a copy of the document
        /// </summary>
        /// <param name="path">Path</param>
        /// <param name="document">Document</param>
        public void SaveACopy(string path, IDocument document)
        {
            // Find a storage
            var storage = FindStorage(document);

            // Save
            storage.Write(path, document);
        }

        /// <summary>
        /// Closes a document editor
        /// </summary>
        /// <param name="editor"></param>
        public void Close(IDocumentEditor editor)
        {
            // Remove from list of opened editors
            _editors.Remove(editor);

            // Close event
            if (DocumentClosed != null)
                DocumentClosed(this, new DocumentClosedEventArgs(editor));
        }

        #endregion

        #region Private functions

        /// <summary>
        /// Attempts to create an editor for the document
        /// </summary>
        /// <param name="document">The document</param>
        /// <exception cref="ArgumentException">Thrown if failed to create editor</exception>
        /// <returns>The editor</returns>
        private IDocumentEditor CreateEditor(IDocument document)
        {
            IDocumentEditor editor = null;

            foreach (var factory in Factories)
                if (factory.CanEdit(document.GetType()))
                {
                    editor = factory.CreateEditor(document);
                    break;
                }

            if (editor == null)
                throw new ArgumentException("Failed to create editor.");

            _editors.Add(editor);
            return editor;
        }

        /// <summary>
        /// Attempts to read a document
        /// </summary>
        /// <param name="path">Path of file</param>
        /// <exception cref="ArgumentException">Thrown when failed to open the document</exception>
        /// <returns></returns>
        private IDocument Read(string path)
        {
            IDocument document = null;

            foreach (var storage in Storages)
                if (storage.CanRead(path))
                {
                    document = storage.Read(path);
                    break;
                }

            // Failed to open
            if (document == null)
                throw new ArgumentException("Failed to open document.");

            return document;
        }

        /// <summary>
        /// Attempts to find a storage for the specified document
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        private IDocumentStorage FindStorage(IDocument document)
        {
            IDocumentStorage storage = null;

            foreach (var s in Storages)
                if (s.CanWrite(document.GetType()))
                {
                    storage = s;
                    break;
                }

            if (storage == null)
                throw new ArgumentException("Failed to find storage object.");

            return storage;
        }

        #endregion
    }
}
