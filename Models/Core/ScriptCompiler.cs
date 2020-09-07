﻿namespace Models.Core
{
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;

    /// <summary>Encapsulates the ability to compile a c# script into an assembly.</summary>
    [Serializable]
    public class ScriptCompiler
    {
        private static bool haveTrappedAssemblyResolveEvent = false;
        private static object haveTrappedAssemblyResolveEventLock = new object();
        private const string tempFileNamePrefix = "APSIM";
        [NonSerialized]
        private CodeDomProvider provider;
        private List<PreviousCompilation> previousCompilations = new List<PreviousCompilation>();

        /// <summary>Constructor.</summary>
        public ScriptCompiler()
        {
            // This looks weird but I'm trying to avoid having to call lock
            // everytime we come through here. If I remove this locking then
            // Jenkins runs very slowly (5 times slower for each sim). Presumably
            // this is because each simulation being run (from APSIMRunner) does the 
            // cleanup below.
            if (!haveTrappedAssemblyResolveEvent)
            {
                lock (haveTrappedAssemblyResolveEventLock)
                {
                    if (!haveTrappedAssemblyResolveEvent)
                    {
                        haveTrappedAssemblyResolveEvent = true;

                        // Trap the assembly resolve event.
                        AppDomain.CurrentDomain.AssemblyResolve -= new ResolveEventHandler(ResolveManagerAssemblies);
                        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ResolveManagerAssemblies);

                        // Clean up apsimx manager .dll files.
                        Cleanup();
                    }
                }
            }
        }

        /// <summary>Compile a c# script.</summary>
        /// <param name="code">The c# code to compile.</param>
        /// <param name="model">The model owning the script.</param>
        /// <param name="referencedAssemblies">Optional referenced assemblies.</param>
        /// <returns>Compile errors or null if no errors.</returns>
        public Results Compile(string code, IModel model, IEnumerable<string> referencedAssemblies = null)
        {
            string errors = null;

            if (code != null)
            {
                // See if we have compiled the code already. If so then no need to compile again.
                var compilation = previousCompilations.Find(c => c.Code == code);

                bool newlyCompiled;
                if (compilation == null || compilation.Code != code)
                {
                    newlyCompiled = true;

                    var assemblies = GetReferenceAssemblies(referencedAssemblies, model.Name);

                    // We haven't compiled the code so do it now.
                    var result = CompileTextToAssembly(code, assemblies);
                    if (result.Errors.Count > 0)
                    {
                        // Errors were found. Add then to the return error string.
                        errors = null;
                        foreach (CompilerError err in result.Errors)
                            errors += $"Line {err.Line}: {err.ErrorText}{Environment.NewLine}";

                        // Because we have errors, remove the previous compilation if there is one.
                        if (compilation != null)
                            previousCompilations.Remove(compilation);
                        compilation = null;
                    }
                    else
                    {
                        // No errors.
                        // If we don't have a previous compilation, create one.
                        if (compilation == null)
                        {
                            compilation = new PreviousCompilation() { ModelFullPath = model.FullPath };
                            previousCompilations.Add(compilation);
                        }

                        // Set the compilation properties.
                        compilation.Code = code;
                        compilation.CompiledAssembly = result.CompiledAssembly;
                    }
                }
                else
                    newlyCompiled = false;

                if (compilation != null)
                {
                    // We have a compiled assembly so get the class name.
                    var regEx = new Regex(@"class\s+(\w+)\s");
                    var match = regEx.Match(code);
                    if (!match.Success)
                        throw new Exception($"Cannot find a class declaration in script:{Environment.NewLine}{code}");
                    var className = match.Groups[1].Value;

                    // Create an instance of the class and give it to the model.
                    var instanceType = compilation.CompiledAssembly.GetTypes().ToList().Find(t => t.Name == className);
                    return new Results(compilation.CompiledAssembly, instanceType.FullName, newlyCompiled);
                }
                else
                    return new Results(errors);
            }

            return null;
        }

        /// <summary>A handler to resolve the loading of manager assemblies when binary deserialization happens.</summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        public static Assembly ResolveManagerAssemblies(object sender, ResolveEventArgs args)
        {
            foreach (string fileName in Directory.GetFiles(Path.GetTempPath(), tempFileNamePrefix + "*.dll"))
                if (args.Name.Split(',')[0] == Path.GetFileNameWithoutExtension(fileName))
                    return Assembly.LoadFrom(fileName);
            return null;
        }

        /// <summary>Gets a list of assembly names that are needed for compiling.</summary>
        /// <param name="referencedAssemblies"></param>
        /// <param name="modelName">Name of model.</param>
        private IEnumerable<string> GetReferenceAssemblies(IEnumerable<string> referencedAssemblies, string modelName)
        {
            IEnumerable<string> references = new string[] 
            {
                "System.dll", 
                "System.Xml.dll", 
                "System.Windows.Forms.dll",
                "System.Data.dll", 
                "System.Core.dll", 
                Assembly.GetExecutingAssembly().Location,
                Assembly.GetEntryAssembly()?.Location,             // Not sure why this can be null in unit tests.
                typeof(MathNet.Numerics.Fit).Assembly.Location,
                typeof(APSIM.Shared.Utilities.MathUtilities).Assembly.Location,
                typeof(Newtonsoft.Json.JsonIgnoreAttribute).Assembly.Location,
            };

            if (previousCompilations != null)
                references = references.Concat(previousCompilations.Where(p => !p.ModelFullPath.Contains($".{modelName}"))
                                                                   .Select(p => p.CompiledAssembly.Location));
            if (referencedAssemblies != null)
                references = references.Concat(referencedAssemblies);
            
            return references.Where(r => r != null);
        }

        /// <summary>
        /// Compile the specified 'code' into an executable assembly. If 'assemblyFileName'
        /// is null then compile to an in-memory assembly.
        /// </summary>
        /// <param name="code">The code to compile.</param>
        /// <param name="referencedAssemblies">Any referenced assemblies.</param>
        /// <returns>Any compile errors or null if compile was successful.</returns>
        private CompilerResults CompileTextToAssembly(string code, IEnumerable<string> referencedAssemblies = null)
        {
            if (provider == null)
                provider = CodeDomProvider.CreateProvider(CodeDomProvider.GetLanguageFromExtension(".cs"));

            var assemblyFileNameToCreate = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), tempFileNamePrefix + Guid.NewGuid().ToString()), ".dll");

            CompilerParameters parameters = new CompilerParameters
            {
                GenerateInMemory = false,
                OutputAssembly = assemblyFileNameToCreate
            };
            string sourceFileName = Path.ChangeExtension(assemblyFileNameToCreate, ".cs");
            File.WriteAllText(sourceFileName, code);

            parameters.OutputAssembly = Path.ChangeExtension(assemblyFileNameToCreate, ".dll");
            parameters.TreatWarningsAsErrors = false;
            parameters.IncludeDebugInformation = true;
            parameters.WarningLevel = 2;
            foreach (var referencedAssembly in referencedAssemblies)
                parameters.ReferencedAssemblies.Add(referencedAssembly);
            parameters.TempFiles = new TempFileCollection(Path.GetTempPath());  // ensure that any temp files are in a writeable area
            parameters.TempFiles.KeepFiles = false;
            return provider.CompileAssemblyFromFile(parameters, new string[] { sourceFileName });
        }

        /// <summary>Cleanup old files.</summary>
        private void Cleanup()
        {
            // Clean up old files.
            var filesToCleanup = new List<string>();
            filesToCleanup.AddRange(Directory.GetFiles(Path.GetTempPath(), $"{tempFileNamePrefix}*.dll"));
            filesToCleanup.AddRange(Directory.GetFiles(Path.GetTempPath(), $"{tempFileNamePrefix}*.cs"));
            filesToCleanup.AddRange(Directory.GetFiles(Path.GetTempPath(), $"{tempFileNamePrefix}*.pdb"));

            foreach (string fileName in filesToCleanup)
            {
                try
                {
                    TimeSpan timeSinceLastAccess = DateTime.Now - File.GetLastAccessTime(fileName);
                    if (timeSinceLastAccess.Hours > 1)
                        File.Delete(fileName);
                }
                catch (Exception)
                {
                    // File locked?
                }
            }
        }

        /// <summary>Encapsulates results from a compile.</summary>
        public class Results
        {
            private Assembly compiledAssembly;
            private string instanceTypeName;

            /// <summary>Constructor.</summary>
            public Results(Assembly assembly, string typeName, bool newlyCompiled)
            {
                compiledAssembly = assembly;
                instanceTypeName = typeName;
                WasCompiled = newlyCompiled;
            }

            /// <summary>Constructor.</summary>
            public Results(string errors)
            {
                WasCompiled = true;
                ErrorMessages = errors;
            }

            /// <summary>Compile error messages. Null for no errors.</summary>
            public string ErrorMessages { get; }

            /// <summary>Was the script compiled or was it already up to date?</summary>
            public bool WasCompiled { get; }

            /// <summary>A newly created instance.</summary>
            public object Instance { get { return compiledAssembly.CreateInstance(instanceTypeName); } }
        }

        /// <summary>Encapsulates a previous compilation.</summary>
        [Serializable]
        private class PreviousCompilation
        {
            /// <summary>The model full path.</summary>
            public string ModelFullPath { get; set; }

            /// <summary>The code that was compiled.</summary>
            public string Code { get; set; }

            /// <summary>The compiled assembly.</summary>
            public Assembly CompiledAssembly { get; set; }
        }
    }
}