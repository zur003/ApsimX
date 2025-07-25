﻿namespace APSIM.Core.Tests
{
    using APSIM.Shared.Utilities;
    using Models;
    using Models.Core;
    using Models.Core.Interfaces;
    using Models.Core.ApsimFile;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Linq;
    using APSIM.Shared.Extensions.Collections;
    using Models.Soils;
    using APSIM.Core;

    /// <summary>
    /// Test the writer's load/save .apsimx capability
    /// </summary>
    [TestFixture]
    public class FileFormatTests
    {
        /// <summary>Test that a simulation can be written to a string.</summary>
        [Test]
        public void FileFormat_WriteToString()
        {
            // Create some models.
            Simulation sim = new Simulation();
            sim.Children.Add(new Clock()
            {
                Name = "Clock",
                StartDate = new DateTime(2015, 1, 1),
                EndDate = new DateTime(2015, 12, 31)
            });
            sim.Children.Add(new Summary()
            {
                Name = "SummaryFile"
            });
            sim.Children.Add(new Manager()
            {
                Name = "Manager",
                Code = ""
            });

            Simulations simulations = new Simulations();
            simulations.Children.Add(sim);
            var node = Node.Create(simulations);

            string json = node.ToJSONString();

            string expectedJson = ReflectionUtilities.GetResourceAsString("UnitTests.Core.ApsimFile.FileFormatTestsReadFromString.json");
            Assert.That(json.Contains("\"$type\": \"Models.Clock, Models\""), Is.True);
            Assert.That(json.Contains("\"Start\": \"2015-01-01T00:00:00\""), Is.True);
            Assert.That(json.Contains("\"End\": \"2015-12-31T00:00:00\""), Is.True);
            Assert.That(json.Contains("\"$type\": \"Models.Summary, Models\""), Is.True);
            Assert.That(json.Contains("\"$type\": \"Models.Manager, Models\""), Is.True);
        }

        /// <summary>Test that a single model can be written to a string. e.g. copy to clipboard.</summary>
        [Test]
        public void FileFormat_WriteSingleModel()
        {
            // Create some models.
            Clock c = new Clock()
            {
                Name = "Clock",
                StartDate = new DateTime(2015, 1, 1),
                EndDate = new DateTime(2015, 12, 31)
            };
            var node = Node.Create(c);

            string json = node.ToJSONString();

            string expectedJson = ReflectionUtilities.GetResourceAsString("UnitTests.APSIM.Core.Resources.FileFormatTestsWriteSingleModel.json");
            Assert.That(json, Is.EqualTo(expectedJson));
        }

        /// <summary>Test that a simulation can be created from a json string.</summary>
        [Test]
        public void FileFormat_ReadFromString()
        {
            string json = ReflectionUtilities.GetResourceAsString("UnitTests.APSIM.Core.Resources.FileFormatTestsReadFromString.json");
            var simulations = FileFormat.ReadFromString<Simulations>(json).Model as Simulations;
            Assert.That(simulations, Is.Not.Null);
            Assert.That(simulations.Children.Count, Is.EqualTo(1));
            var simulation = simulations.Children[0];
            Assert.That(simulation.Parent, Is.EqualTo(simulations));
            Assert.That(simulation.Children.Count, Is.EqualTo(3));
            Assert.That(simulation.Children[0].Name, Is.EqualTo("Clock"));
            Assert.That(simulation.Children[0].Parent, Is.EqualTo(simulation));
            Assert.That((simulation.Children[0] as Clock).StartDate, Is.EqualTo(new DateTime(2015, 1, 1)));
            Assert.That(simulation.Children[1].Name, Is.EqualTo("SummaryFile"));
            Assert.That(simulation.Children[1].Parent, Is.EqualTo(simulation));
            Assert.That(simulation.Children[2].Name, Is.EqualTo("Manager"));
            Assert.That(simulation.Children[2].Parent, Is.EqualTo(simulation));
        }

        /// <summary>Test that a model can throw during creation and that it is captured.</summary>
        [Test]
        public void FileFormat_CheckThatModelsCanThrowExceptionsDuringCreation()
        {
            string json = ReflectionUtilities.GetResourceAsString("UnitTests.APSIM.Core.Resources.FileFormatTestsCheckThatModelsCanThrowExceptionsDuringCreation.json");
            List<Exception> creationExceptions = new List<Exception>();
            var simulations = FileFormat.ReadFromString<Simulations>(json, e => creationExceptions.Add(e), false).Model as Simulations;
            Assert.That(creationExceptions.Count, Is.EqualTo(1));
            Assert.That(creationExceptions[0].Message.StartsWith("Errors found"), Is.True);

            // Even though the manager model threw an exception we should still have
            // a valid simulation.
            Assert.That(simulations, Is.Not.Null);
            Assert.That(simulations.Children.Count, Is.EqualTo(1));
            var simulation = simulations.Children[0];
            Assert.That(simulation.Parent, Is.EqualTo(simulations));
            Assert.That(simulation.Children.Count, Is.EqualTo(2));
            Assert.That(simulation.Children[0].Name, Is.EqualTo("Clock"));
            Assert.That(simulation.Children[0].Parent, Is.EqualTo(simulation));
            Assert.That((simulation.Children[0] as Clock).StartDate, Is.EqualTo(new DateTime(2015, 1, 1)));
            Assert.That(simulation.Children[1].Name, Is.EqualTo("Manager"));
            Assert.That(simulation.Children[1].Parent, Is.EqualTo(simulation));
        }

        /// <summary>
        /// This test ensures that exceptions thrown while opening a file cause
        /// the run to be flagged as failed.
        /// </summary>
        [Test]
        public void OnCreatedShouldFailRun()
        {
            // Redirect console to stop the exception text (written by the call to Main below) from
            // being sent to Jenkins console output.
            StringWriter sw = new StringWriter();
            Console.SetOut(sw);

            string json = ReflectionUtilities.GetResourceAsString("UnitTests.APSIM.Core.Resources.OnCreatedError.apsimx");
            string fileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".apsimx");
            File.WriteAllText(fileName, json);

            int result = Models.Program.Main(new[] { fileName });
            Assert.That(result, Is.EqualTo(1));
        }

        /// <summary>Test that the example files can be loaded and saved without error.</summary>
        [Test]
        public void LoadAndSaveExamples()
        {
            bool allFilesHaveRootReference = true;
            string binDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string exampleFileDirectory = Path.GetFullPath(Path.Combine(binDirectory, "..", "..", "..", "Examples"));
            IEnumerable<string> exampleFileNames = Directory.GetFiles(exampleFileDirectory, "*.apsimx", SearchOption.AllDirectories);
            foreach (string exampleFile in exampleFileNames)
            {
                var node = FileFormat.ReadFromFile<Simulations>(exampleFile);
                node.ToJSONString();
            }
            Assert.That(allFilesHaveRootReference, Is.True);
        }

        /// <summary>Test that a simulation can be created from a json string.</summary>
        [Test]
        public void FileFormat_ReadAPSoilFile()
        {
            string xml = ReflectionUtilities.GetResourceAsString("UnitTests.APSIM.Core.Resources.Apsoil.soil");

            string fileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".apsimx");
            File.WriteAllText(fileName, xml);

            var soil = FileFormat.ReadFromFile<Soil>(fileName).Model as Soil;
            Assert.That(soil.Name, Is.EqualTo("APSoil"));
        }
    }
}
