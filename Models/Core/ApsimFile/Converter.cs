﻿namespace Models.Core.ApsimFile
{
    using APSIM.Shared.Utilities;
    using Models.Functions;
    using Models.LifeCycle;
    using Models.PMF;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml;

    /// <summary>
    /// Converts the .apsim file from one version to the next
    /// </summary>
    public class Converter
    {
        /// <summary>Gets the latest .apsimx file format version.</summary>
        public static int LatestVersion { get { return 100; } }

        /// <summary>Converts a .apsimx string to the latest version.</summary>
        /// <param name="st">XML or JSON string to convert.</param>
        /// <param name="toVersion">The optional version to convert to.</param>
        /// <param name="fileName">The optional filename where the string came from.</param>
        /// <returns>Returns true if something was changed.</returns>
        public static ConverterReturnType DoConvert(string st, int toVersion = -1, string fileName = null)
        {
            ConverterReturnType returnData = new ConverterReturnType();

            if (toVersion == -1)
                toVersion = LatestVersion;

            int offset = st.TakeWhile(c => char.IsWhiteSpace(c)).Count();
            char firstNonBlankChar = st[offset];

            if (firstNonBlankChar == '<')
            {
                bool changed = XmlConverters.DoConvert(ref st, Math.Min(toVersion, XmlConverters.LastVersion), fileName);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(st);
                int fileVersion = Convert.ToInt32(XmlUtilities.Attribute(doc.DocumentElement, "Version"), CultureInfo.InvariantCulture);
                if (fileVersion == toVersion)
                    return new ConverterReturnType()
                    { DidConvert = changed, RootXml = doc };

                st = ConvertToJSON(st, fileName);
                returnData.Root = JObject.Parse(st);
            }
            else if (firstNonBlankChar == '{')
            {
                // json
                returnData.Root = JObject.Parse(st);
            }
            else
            {
                throw new Exception("Unknown string encountered. Not JSON or XML. String: " + st);
            }

            if (returnData.Root.ContainsKey("Version"))
            {
                int fileVersion = (int)returnData.Root["Version"];

                if (fileVersion > LatestVersion)
                    throw new Exception(string.Format("Unable to open file '{0}'. File version is greater than the latest file version. Has this file been opened in a more recent version of Apsim?", fileName));

                // Run converters if not at the latest version.
                while (fileVersion < toVersion)
                {
                    returnData.DidConvert = true;

                    // Find the method to call to upgrade the file by one version.
                    int versionFunction = fileVersion + 1;
                    MethodInfo method = typeof(Converter).GetMethod("UpgradeToVersion" + versionFunction, BindingFlags.NonPublic | BindingFlags.Static);
                    if (method == null)
                        throw new Exception("Cannot find converter to go to version " + versionFunction);

                    // Found converter method so call it.
                    method.Invoke(null, new object[] { returnData.Root, fileName });

                    fileVersion++;
                }

                if (returnData.DidConvert)
                {
                    returnData.Root["Version"] = fileVersion;
                    st = returnData.Root.ToString();
                }
            }
            returnData.DidConvert = EnsureSoilHasInitWaterAndSample(returnData.Root) || returnData.DidConvert;

            return returnData;
        }

        /// <summary>
        /// If root is a soil then make sure it has a sample or init water.
        /// </summary>
        /// <param name="root">The root node of the JSON to look at.</param>
        /// <returns>True if model was changed.</returns>
        private static bool EnsureSoilHasInitWaterAndSample(JObject root)
        {
            string rootType = JsonUtilities.Type(root, true);

            if (rootType != null && rootType == "Models.Soils.Soil")
            {
                JArray soilChildren = root["Children"] as JArray;
                if (soilChildren != null && soilChildren.Count > 0)
                {
                    var initWater = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".InitWater"));
                    var sample = soilChildren.FirstOrDefault(c => c["$type"].Value<string>().Contains(".Sample"));

                    if (sample == null && initWater == null)
                    {
                        // Add in an initial water and initial conditions models.
                        initWater = new JObject();
                        initWater["$type"] = "Models.Soils.InitialWater, Models";
                        JsonUtilities.RenameModel(initWater as JObject, "Initial water");
                        initWater["PercentMethod"] = "FilledFromTop";
                        initWater["FractionFull"] = 1;
                        initWater["DepthWetSoil"] = "NaN";
                        soilChildren.Add(initWater);

                        sample = new JObject();
                        sample["$type"] = "Models.Soils.Sample, Models";
                        JsonUtilities.RenameModel(sample as JObject, "Initial conditions");
                        sample["Thickness"] = new JArray(new double[] { 1800 });
                        sample["NO3N"] = new JArray(new double[] { 3 });
                        sample["NH4"] = new JArray(new double[] { 1 });
                        sample["SWUnits"] = "Volumetric";
                        soilChildren.Add(sample);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Upgrades to version 47 - the first JSON version.</summary>
        private static string ConvertToJSON(string st, string fileName)
        {
            string json = XmlToJson.Convert(st);
            JObject j = JObject.Parse(json);
            j["Version"] = 47;
            return j.ToString();
        }

        private static void UpgradeToVersion47(JObject root, string fileName)
        {
            // Nothing to do as conversion to JSON has already happened.
        }

        /// <summary>
        /// Upgrades to version 48. Iterates through all manager scripts, and replaces
        /// all instances of the text "DisplayTypeEnum" with "DisplayType".
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion48(JObject root, string fileName)
        {
            foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
                JsonUtilities.ReplaceManagerCode(manager, "DisplayTypeEnum", "DisplayType");
        }


        /// <summary>
        /// Upgrades to version 49. Renames Models.Morris+Parameter to Models.Sensitivity.Parameter.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion49(JObject root, string fileName)
        {
            foreach (JObject morris in JsonUtilities.ChildrenRecursively(root, "Models.Morris"))
                foreach (var parameter in morris["Parameters"])
                    parameter["$type"] = parameter["$type"].ToString().Replace("Models.Morris+Parameter", "Models.Sensitivity.Parameter");
        }

        ///<summary>
        /// Upgrades to version 50. Fixes the RelativeTo property of 
        /// InitialWater components of soils copied from Apsim Classic.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        /// <remarks>
        /// ll15 must be renamed to LL15.
        /// Wheat must be renamed to WheatSoil.
        /// Maize must be renamed to MaizeSoil.
        /// </remarks>
        private static void UpgradeToVersion50(JObject root, string fileName)
        {
            foreach (JObject initialWater in JsonUtilities.ChildrenRecursively(root, "InitialWater"))
            {
                if (initialWater["RelativeTo"] != null)
                {
                    if (initialWater["RelativeTo"].ToString().ToUpper().Contains("LL15"))
                        initialWater["RelativeTo"] = initialWater["RelativeTo"].ToString().Replace("ll15", "LL15");
                    else if (!string.IsNullOrEmpty(initialWater["RelativeTo"].ToString()) && !initialWater["RelativeTo"].ToString().EndsWith("Soil"))
                        initialWater["RelativeTo"] = initialWater["RelativeTo"].ToString() + "Soil";
                }
            }
        }
        /// <summary>
        /// Changes GsMax to Gsmax350 in all models that implement ICanopy.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion51(JObject root, string fileName)
        {
            // Create a list of models that might have gsmax.
            // Might need to add in other models that implement ICanopy 
            // e.g. OilPalm, AgPastureSpecies, SimpleTree, Sugarcane

            var models = new List<JObject>();
            models.AddRange(JsonUtilities.ChildrenOfType(root, "Leaf"));
            models.AddRange(JsonUtilities.ChildrenOfType(root, "SimpleLeaf"));
            models.AddRange(JsonUtilities.ChildrenOfType(root, "PerennialLeaf"));
            models.AddRange(JsonUtilities.ChildrenOfType(root, "SorghumLeaf"));

            // Loop through all models and rename Gsmax to Gsmax350.
            foreach (var model in models)
            {
                JsonUtilities.RenameProperty(model, "Gsmax", "Gsmax350");
                JsonUtilities.AddConstantFunctionIfNotExists(model, "StomatalConductanceCO2Modifier", "1.0");
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion52(JObject root, string fileName)
        {
            foreach (var SOM in JsonUtilities.ChildrenOfType(root, "SoilOrganicMatter"))
            {
                double rootWt;
                if (SOM["RootWt"] is JArray)
                    rootWt = Convert.ToDouble(SOM["RootWt"][0], CultureInfo.InvariantCulture); // This can happen when importing old APSIM file.
                else
                    rootWt = Convert.ToDouble(SOM["RootWt"], CultureInfo.InvariantCulture);
                SOM.Remove("RootWt");
                double[] thickness = MathUtilities.StringsToDoubles(JsonUtilities.Values(SOM, "Thickness"));

                double profileDepth = MathUtilities.Sum(thickness);
                double cumDepth = 0;
                double[] rootWtFraction = new double[thickness.Length];

                for (int layer = 0; layer < thickness.Length; layer++)
                {
                    double fracLayer = Math.Min(1.0, MathUtilities.Divide(profileDepth - cumDepth, thickness[layer], 0.0));
                    cumDepth += thickness[layer];
                    rootWtFraction[layer] = fracLayer * Math.Exp(-3.0 * Math.Min(1.0, MathUtilities.Divide(cumDepth, profileDepth, 0.0)));
                }
                // get the actuall FOM distribution through layers (adds up to one)
                double totFOMfraction = MathUtilities.Sum(rootWtFraction);
                for (int layer = 0; layer < thickness.Length; layer++)
                    rootWtFraction[layer] /= totFOMfraction;
                double[] rootWtVector = MathUtilities.Multiply_Value(rootWtFraction, rootWt);

                JsonUtilities.SetValues(SOM, "RootWt", rootWtVector);
            }

        }

        /// <summary>
        /// Adds solutes under SoilNitrogen.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion53(JObject root, string fileName)
        {
            foreach (var soilNitrogen in JsonUtilities.ChildrenOfType(root, "SoilNitrogen"))
            {
                JsonUtilities.CreateNewChildModel(soilNitrogen, "NO3", "Models.Soils.SoilNitrogenNO3");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "NH4", "Models.Soils.SoilNitrogenNH4");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "Urea", "Models.Soils.SoilNitrogenUrea");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "PlantAvailableNO3", "Models.Soils.SoilNitrogenPlantAvailableNO3");
                JsonUtilities.CreateNewChildModel(soilNitrogen, "PlantAvailableNH4", "Models.Soils.SoilNitrogenPlantAvailableNH4");
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NO3", "SoilNitrogen.NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NH4", "SoilNitrogen.NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.urea", "SoilNitrogen.Urea.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.PlantAvailableNO3", "SoilNitrogen.PlantAvailableNO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.PlantAvailableNH4", "SoilNitrogen.PlantAvailableNH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].no3", "[SoilNitrogen].NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].nh4", "[SoilNitrogen].NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].urea", "[SoilNitrogen].Urea.kgha");
            }
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                var originalCode = manager.ToString();
                if (originalCode != null)
                {
                    if (originalCode.Contains("SoilNitrogen.NO3"))
                    {
                        manager.Replace("Soil.SoilNitrogen.NO3", "NO3.kgha");
                        manager.Replace("SoilNitrogen.NO3", "NO3.kgha");
                        manager.AddDeclaration("ISolute", "NO3", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.NH4"))
                    {
                        manager.Replace("Soil.SoilNitrogen.NH4", "NH4.kgha");
                        manager.Replace("SoilNitrogen.NH4", "NH4.kgha");
                        manager.AddDeclaration("ISolute", "NH4", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.urea"))
                    {
                        manager.Replace("Soil.SoilNitrogen.urea", "Urea.kgha");
                        manager.Replace("SoilNitrogen.urea", "Urea.kgha");
                        manager.AddDeclaration("ISolute", "Urea", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.PlantAvailableNO3"))
                    {
                        manager.Replace("Soil.SoilNitrogen.PlantAvailableNO3", "PlantAvailableNO3.kgha");
                        manager.Replace("SoilNitrogen.PlantAvailableNO3", "PlantAvailableNO3.kgha");
                        manager.AddDeclaration("ISolute", "PlantAvailableNO3", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode.Contains("SoilNitrogen.PlantAvailableNH4"))
                    {
                        manager.Replace("Soil.SoilNitrogen.PlantAvailableNH4", "PlantAvailableNH4.kgha");
                        manager.Replace("SoilNitrogen.PlantAvailableNH4", "PlantAvailableNH4.kgha");
                        manager.AddDeclaration("ISolute", "PlantAvailableNH4", new string[] { "[ScopedLinkByName]" });
                    }
                    if (originalCode != manager.ToString())
                    {
                        var usingLines = manager.GetUsingStatements().ToList();
                        usingLines.Add("Models.Interfaces");
                        manager.SetUsingStatements(usingLines);
                        manager.Save();
                    }
                }
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NO3", "SoilNitrogen.NO3.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NH4", "SoilNitrogen.NH4.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.urea", "SoilNitrogen.Urea.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNO3", "SoilNitrogen.PlantAvailableNO3.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNH4", "SoilNitrogen.PlantAvailableNH4.kgha");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NO3", "SoilNitrogen.NO3.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NH4", "SoilNitrogen.NH4.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.urea", "SoilNitrogen.Urea.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNO3", "SoilNitrogen.PlantAvailableNO3.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNH4", "SoilNitrogen.PlantAvailableNH4.kgha");
                }
            }
        }

        /// <summary>
        /// Remove SoluteManager.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion54(JObject root, string fileName)
        {
            foreach (var soluteManager in JsonUtilities.ChildrenOfType(root, "SoluteManager"))
                soluteManager.Remove();

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].NO3N", "[Soil].SoilNitrogen.NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].NH4N", "[Soil].SoilNitrogen.NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].UreaN", "[Soil].SoilNitrogen.Urea.kgha");
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                bool managerChanged = false;
                if (manager.Replace("mySoil.NO3N", "NO3.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NO3", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("mySoil.NH4N", "NH4.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NH4", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("mySoil.UreaN", "Urea.kgha"))
                {
                    manager.AddDeclaration("ISolute", "Urea", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.NO3N", "NO3.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NO3", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.NH4N", "NH4.kgha"))
                {
                    manager.AddDeclaration("ISolute", "NH4", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.UreaN", "Urea.kgha"))
                {
                    manager.AddDeclaration("ISolute", "Urea", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("mySoil.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("Soil.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("soil.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                if (manager.Replace("soil1.SoilNitrogen.", "SoilNitrogen."))
                {
                    manager.AddDeclaration("SoilNitrogen", "SoilNitrogen", new string[] { "[ScopedLinkByName]" });
                    managerChanged = true;
                }
                var declarations = manager.GetDeclarations();
                if (declarations.RemoveAll(declaration => declaration.TypeName == "SoluteManager") > 0)
                {
                    manager.SetDeclarations(declarations);
                    managerChanged = true;
                }

                if (managerChanged)
                {
                    var usingLines = manager.GetUsingStatements().ToList();
                    usingLines.Add("Models.Interfaces");
                    manager.SetUsingStatements(usingLines);
                    manager.Save();
                }
            }
        }


        /// <summary>
        /// Changes initial Root Wt to an array.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion55(JObject root, string fileName)
        {
            foreach (var SOM in JsonUtilities.ChildrenOfType(root, "SoilOrganicMatter"))
            {
                double soilcnr;
                if (SOM["SoilCN"] is JArray)
                    soilcnr = Convert.ToDouble(SOM["SoilCN"][0], CultureInfo.InvariantCulture); // This can happen when importing old APSIM file.
                else
                    soilcnr = Convert.ToDouble(SOM["SoilCN"], CultureInfo.InvariantCulture);
                SOM.Remove("SoilCN");
                double[] thickness = MathUtilities.StringsToDoubles(JsonUtilities.Values(SOM, "Thickness"));

                double[] SoilCNVector = new double[thickness.Length];

                for (int layer = 0; layer < thickness.Length; layer++)
                    SoilCNVector[layer] = soilcnr;

                JsonUtilities.SetValues(SOM, "SoilCN", SoilCNVector);
            }

        }

        /// <summary>
        /// Change Factor.Specifications to Factor.Specification. Also FactorValue
        /// becomes CompositeFactor.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion56(JToken root, string fileName)
        {
            foreach (var factor in JsonUtilities.ChildrenRecursively(root as JObject, "Factor"))
            {
                var parent = JsonUtilities.Parent(factor);

                string parentModelType = JsonUtilities.Type(parent);
                if (parentModelType == "Factors")
                {
                    var specifications = factor["Specifications"] as JArray;
                    if (specifications != null)
                    {
                        if (specifications.Count > 1)
                        {
                            // must be a compound factor. 

                            // Change our Factor to a CompositeFactor
                            factor["$type"] = "Models.Factorial.CompositeFactor, Models";

                            // Remove the Factor from it's parent.
                            var parentChildren = parent["Children"] as JArray;
                            parentChildren.Remove(factor);

                            // Create a new site factor and add our CompositeFactor to the children list.
                            var siteFactor = JsonUtilities.ChildWithName(parent as JObject, "Site") as JObject;
                            if (siteFactor == null)
                            {
                                // Create a site factor 
                                siteFactor = new JObject();
                                siteFactor["$type"] = "Models.Factorial.Factor, Models";
                                JsonUtilities.RenameModel(siteFactor, "Site");
                                JArray siteFactorChildren = new JArray();
                                siteFactor["Children"] = siteFactorChildren;

                                // Add our new site factor to our models parent.
                                parentChildren.Add(siteFactor);
                            }
                            (siteFactor["Children"] as JArray).Add(factor);

                        }
                        else
                        {
                            // Convert array to string.
                            if (specifications.Count > 0)
                                factor["Specification"] = specifications[0].ToString();
                        }
                    }
                }
                else if (parentModelType == "Factor")
                {
                    factor["$type"] = "Models.Factorial.CompositeFactor, Models";
                }
            }

            foreach (var series in JsonUtilities.ChildrenRecursively(root as JObject, "Series"))
            {
                var factorToVaryColours = series["FactorToVaryColours"];
                if (factorToVaryColours != null && factorToVaryColours.Value<string>() == "Simulation")
                    series["FactorToVaryColours"] = "SimulationName";
                var factorToVaryMarkers = series["FactorToVaryMarkers"];
                if (factorToVaryMarkers != null && factorToVaryMarkers.Value<string>() == "Simulation")
                    series["FactorToVaryMarkers"] = "SimulationName";
                var factorToVaryLines = series["FactorToVaryLines"];
                if (factorToVaryLines != null && factorToVaryLines.Value<string>() == "Simulation")
                    series["FactorToVaryLines"] = "SimulationName";
            }
        }

        /// <summary>
        /// Upgrades to version 57. Adds a RetranslocateNonStructural node to
        /// all GenericOrgans which do not have a child called
        /// RetranslocateNitrogen.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion57(JObject root, string fileName)
        {
            foreach (JObject organ in JsonUtilities.ChildrenRecursively(root, "GenericOrgan"))
                if (JsonUtilities.ChildWithName(organ, "RetranslocateNitrogen") == null)
                    JsonUtilities.AddModel(organ, typeof(RetranslocateNonStructural), "RetranslocateNitrogen");
        }

        /// <summary>
        /// Upgrades to version 58. Renames 'ParamThickness' to 'Thickness' in Weirdo.
        /// Also change calls to property soil.SWAtWaterThickness to soil.Thickness.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion58(JObject root, string fileName)
        {
            foreach (JObject weirdo in JsonUtilities.ChildrenRecursively(root, "WEIRDO"))
            {
                var paramThicknessNode = weirdo["ParamThickness"];
                if (paramThicknessNode != null)
                {
                    weirdo["Thickness"] = paramThicknessNode;
                    weirdo.Remove("ParamThickness");
                }
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                if (manager.Replace(".SWAtWaterThickness", ".Thickness"))
                    manager.Save();
            }
        }

        /// <summary>
        /// Upgrades to version 59. Renames 'SoilCropOilPalm' to 'SoilCrop'.
        /// Renames Soil.SoilOrganicMatter.OC to Soil.Initial.OC
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion59(JObject root, string fileName)
        {
            foreach (var sample in JsonUtilities.ChildrenRecursively(root, "Sample"))
            {
                var array = sample["NO3"] as JArray;
                if (array != null)
                {
                    var nitrogenValue = new JObject();
                    nitrogenValue["$type"] = "Models.Soils.NitrogenValue, Models";

                    var storedAsPPM = sample["NO3Units"]?.ToString() == "0" ||
                                      sample["NO3Units"]?.ToString() == "ppm" ||
                                      sample["NO3Units"] == null;

                    nitrogenValue["Values"] = array;
                    nitrogenValue["StoredAsPPM"] = storedAsPPM;
                    sample.Remove("NO3");
                    sample["NO3N"] = nitrogenValue;
                }

                array = sample["NH4"] as JArray;
                if (array != null)
                {
                    var nitrogenValue = new JObject();
                    nitrogenValue["$type"] = "Models.Soils.NitrogenValue, Models";

                    var storedAsPPM = sample["NH4Units"]?.ToString() == "0" ||
                                      sample["NH4Units"]?.ToString() == "ppm" ||
                                      sample["NH4Units"] == null;

                    nitrogenValue["Values"] = array;
                    nitrogenValue["StoredAsPPM"] = storedAsPPM;
                    sample.Remove("NH4");
                    sample["NH4N"] = nitrogenValue;
                }
            }
            foreach (var soilCropOilPalmNode in JsonUtilities.ChildrenRecursively(root, "SoilCropOilPalm"))
                soilCropOilPalmNode["$type"] = "Models.Soils.SoilCrop, Models";

            foreach (var report in JsonUtilities.ChildrenRecursively(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilOrganicMatter.OC", ".Initial.OC");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].PH", "[Soil].Initial.PH");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].EC", "[Soil].Initial.EC");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].ESP", "[Soil].Initial.ESP");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].Cl", "[Soil].Initial.CL");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].OC", "[Soil].Initial.OC");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].InitialNO3N", "[Soil].Initial.NO3N.PPM");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[Soil].InitialNH4N", "[Soil].Initial.NH4N.PPM");
            }

            foreach (var series in JsonUtilities.ChildrenRecursively(root, "Series"))
            {
                if (series["XFieldName"] != null)
                    series["XFieldName"] = series["XFieldName"].ToString().Replace(".SoilOrganicMatter.OC", ".Initial.OC");
                if (series["YFieldName"] != null)
                    series["YFieldName"] = series["YFieldName"].ToString().Replace(".SoilOrganicMatter.OC", ".Initial.OC");
            }

            foreach (var expressionFunction in JsonUtilities.ChildrenRecursively(root, "ExpressionFunction"))
            {
                var expression = expressionFunction["Expression"].ToString();
                expression = expression.Replace(".SoilOrganicMatter.OC", ".Initial.OC");
                expressionFunction["Expression"] = expression;
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                var changeMade = manager.Replace("Soil.ToCumThickness(soil.Thickness)", "soil.ThicknessCumulative");

                if (manager.Replace("mySoil.Depth.Length", "mySoil.Thickness.Length"))
                    changeMade = true;

                if (manager.Replace("soil.Depth.Length", "soil.Thickness.Length"))
                    changeMade = true;

                if (changeMade)
                    manager.Save();
            }
        }

        /// <summary>
        /// Convert no3 and nh4 parameters from ppm to kg/ha.
        /// </summary>
        /// <param name="values"></param>
        private static void ConvertToPPM(JArray values)
        {
            var sample = JsonUtilities.Parent(JsonUtilities.Parent(values));
            var soil = JsonUtilities.Parent(sample) as JObject;
            var water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "Water");
            if (water == null)
                water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "WEIRDO");

            // Get soil thickness and bulk density.
            var soilThickness = water["Thickness"].Values<double>().ToArray();
            var soilBD = water["BD"].Values<double>().ToArray();

            // Get sample thickness and bulk density.
            var sampleThickness = sample["Thickness"].Values<double>().ToArray();
            var sampleBD = Soils.Standardiser.Layers.MapConcentration(soilBD, soilThickness, sampleThickness, soilBD.Last());

            for (int i = 0; i < values.Count; i++)
                values[i] = values[i].Value<double>() * 100 / (sampleBD[i] * sampleThickness[i]);
        }

        /// <summary>
        /// Does the specified array have non NaN values?
        /// </summary>
        /// <param name="no3Values">The array to remove them from.</param>
        private static bool HasValues(JArray no3Values)
        {
            foreach (var value in no3Values)
                if (value.ToString() != "NaN")
                    return true;
            return false;
        }

        /// <summary>
        /// Upgrades to version 60. Move NO3 and NH4 from sample to Analaysis node
        /// and always store as ppm.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion60(JObject root, string fileName)
        {
            foreach (var sample in JsonUtilities.ChildrenRecursively(root, "Sample"))
            {
                var soil = JsonUtilities.Parent(sample) as JObject;
                var analysis = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "Analysis");
                var water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "Water");
                if (water == null)
                    water = JsonUtilities.Children(soil).Find(child => JsonUtilities.Type(child) == "WEIRDO");

                var no3Node = sample["NO3N"];
                if (no3Node != null && no3Node.HasValues)
                {
                    if (analysis == null)
                        throw new Exception("Cannot find an analysis node while converting a soil sample.");

                    // Convert units to ppm if necessary.
                    var no3Values = no3Node["Values"] as JArray;

                    // Only overlay values if they are not NaN values.
                    if (HasValues(no3Values))
                    {
                        if (!no3Node["StoredAsPPM"].Value<bool>())
                            ConvertToPPM(no3Values);

                        // Make sure layers match analysis layers.
                        var analysisThickness = analysis["Thickness"].Values<double>().ToArray();
                        var sampleThickness = sample["Thickness"].Values<double>().ToArray();
                        var values = no3Values.Values<double>().ToArray();
                        var mappedValues = Soils.Standardiser.Layers.MapConcentration(values, sampleThickness, analysisThickness, 1.0);
                        no3Values = new JArray(mappedValues);

                        // Move from sample to analysis
                        analysis["NO3N"] = no3Values;
                    }
                }
                sample["NO3N"] = null;
                var nh4Node = sample["NH4N"];
                if (nh4Node != null && nh4Node.HasValues)
                {
                    if (analysis == null)
                        throw new Exception("Cannot find an analysis node while converting a soil sample.");

                    // Convert units to ppm if necessary.
                    var nh4Values = nh4Node["Values"] as JArray;

                    // Only overlay values if they are not NaN values.
                    if (HasValues(nh4Values))
                    {
                        if (!nh4Node["StoredAsPPM"].Value<bool>())
                            ConvertToPPM(nh4Values);

                        // Make sure layers match analysis layers.
                        var analysisThickness = analysis["Thickness"].Values<double>().ToArray();
                        var sampleThickness = sample["Thickness"].Values<double>().ToArray();
                        var values = nh4Values.Values<double>().ToArray();
                        var mappedValues = Soils.Standardiser.Layers.MapConcentration(values, sampleThickness, analysisThickness, 0.2);
                        nh4Values = new JArray(mappedValues);

                        // Move from sample to analysis
                        analysis["NH4N"] = nh4Values;
                    }
                }
                sample["NH4N"] = null;
            }
        }

        /// <summary>
        /// Upgrade to version 60. Ensures that a micromet model is within every simulation.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion61(JObject root, string fileName)
        {
            foreach (JObject Sim in JsonUtilities.ChildrenRecursively(root, "Simulation"))
            {
                List<JObject> MicroClimates = JsonUtilities.ChildrenRecursively(root, "MicroClimate");
                if (MicroClimates.Count == 0)
                    AddMicroClimate(Sim);
            }

        }

        /// <summary>
        /// Add a MicroClimate model to the specified JSON model token.
        /// </summary>
        /// <param name="simulation">An APSIM Simulation</param>
        public static void AddMicroClimate(JObject simulation)
        {
            JArray children = simulation["Children"] as JArray;
            if (children == null)
            {
                children = new JArray();
                simulation["Children"] = children;
            }

            JObject microClimateModel = new JObject();
            microClimateModel["$type"] = "Models.MicroClimate, Models";
            JsonUtilities.RenameModel(microClimateModel, "MicroClimate");
            microClimateModel["a_interception"] = "0.0";
            microClimateModel["b_interception"] = "1.0";
            microClimateModel["c_interception"] = "0.0";
            microClimateModel["d_interception"] = "0.0";
            microClimateModel["soil_albedo"] = "0.13";
            microClimateModel["SoilHeatFluxFraction"] = "0.4";
            microClimateModel["NightInterceptionFraction"] = "0.5";
            microClimateModel["ReferenceHeight"] = "2.0";
            microClimateModel["IncludeInDocumentation"] = "true";
            microClimateModel["Enabled"] = "true";
            microClimateModel["ReadOnly"] = "false";
            var weathers = JsonUtilities.ChildrenOfType(simulation, "Weather");

            // Don't bother with microclimate if no weather component
            if (weathers.Count != 0)
            {
                var weather = weathers.First();
                int index = children.IndexOf(weather);
                children.Insert(index + 1, microClimateModel);
            }
        }

        /// <summary>
        /// Upgrades to version 62. Fixes SimpleLeaf variable names
        /// following a refactor of this class.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion62(JObject root, string fileName)
        {
            // We renamed a lot of IFunctions and removed the 'Function' suffix.
            // ie HeightFunction -> Height.
            Dictionary<string, string> changedProperties = new Dictionary<string, string>();
            changedProperties.Add("Tallness", "HeightFunction");
            changedProperties.Add("Area", "LAIFunction");
            changedProperties.Add("LaiDead", "LaiDeadFunction");
            changedProperties.Add("WaterDemand", "WaterDemandFunction");
            changedProperties.Add("Cover", "CoverFunction");
            changedProperties.Add("ExtinctionCoefficient", "ExtinctionCoefficientFunction");
            changedProperties.Add("BaseHeight", "BaseHeightFunction");
            changedProperties.Add("Wideness", "WidthFunction");
            changedProperties.Add("DetachmentRate", "DetachmentRateFunction");
            changedProperties.Add("InitialWt", "InitialWtFunction");
            changedProperties.Add("MaintenanceRespiration", "MaintenanceRespirationFunction");
            changedProperties.Add("FRGR", "FRGRFunction");

            // Names of nodes which are probably simple leaf. The problem is that
            // in released models, the model is stored in a separate file to the
            // simulations. Therefore when we parse/convert the simulation file,
            // we don't know the names of the simple leaf models, so we are forced
            // take a guess.
            List<string> modelNames = new List<string>() { "Leaf", "Stover" };

            // Names of nodes which are definitely simple leaf.
            List<string> definiteSimpleLeaves = new List<string>();

            // Go through all SimpleLeafs and rename the appropriate children.
            foreach (JObject leaf in JsonUtilities.ChildrenRecursively(root, "SimpleLeaf"))
            {
                modelNames.Add(leaf["Name"].ToString());
                definiteSimpleLeaves.Add(leaf["Name"].ToString());
                // We removed the Leaf.AppearedCohortNo property.
                JObject relativeArea = JsonUtilities.FindFromPath(leaf, "DeltaLAI.Vegetative.Delta.RelativeArea");
                if (relativeArea != null && relativeArea["XProperty"].ToString() == "[Leaf].AppearedCohortNo")
                    relativeArea["XProperty"] = "[Leaf].NodeNumber";

                foreach (var change in changedProperties)
                {
                    string newName = change.Key;
                    string old = change.Value;
                    JsonUtilities.RenameChildModel(leaf, old, newName);
                }
            }

            foreach (JObject reference in JsonUtilities.ChildrenRecursively(root, "VariableReference"))
            {
                foreach (string leafName in definiteSimpleLeaves)
                {
                    foreach (KeyValuePair<string, string> property in changedProperties)
                    {
                        string oldName = property.Value;
                        string newName = property.Key;

                        string toReplace = $"{leafName}.{oldName}";
                        string replaceWith = $"{leafName}.{newName}";
                        reference["VariableName"] = reference["VariableName"].ToString().Replace(toReplace, replaceWith);

                        toReplace = $"[{leafName}].{oldName}";
                        replaceWith = $"[{leafName}].{newName}";
                        reference["VariableName"] = reference["VariableName"].ToString().Replace(toReplace, replaceWith);
                    }
                }
            }

            // Attempt some basic find/replace in manager scripts.
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                foreach (var change in changedProperties)
                {
                    string newName = change.Key;
                    string old = change.Value;

                    bool changed = false;
                    foreach (string modelName in modelNames)
                    {
                        string toReplace = $"{modelName}.{old}";
                        string replaceWith = $"{modelName}.{newName}";
                        changed |= manager.Replace(toReplace, replaceWith, true);

                        foreach (KeyValuePair<string, string> parameter in manager.Parameters)
                        {
                            string newParam = parameter.Value.Replace(toReplace, replaceWith);
                            manager.UpdateParameter(parameter.Key, newParam);
                        }

                        toReplace = $"[{modelName}].{old}";
                        replaceWith = $"[{modelName}].{newName}";
                        changed |= manager.Replace(toReplace, replaceWith, true);

                        foreach (KeyValuePair<string, string> parameter in manager.Parameters)
                        {
                            string newParam = parameter.Value.Replace(toReplace, replaceWith);
                            manager.UpdateParameter(parameter.Key, newParam);
                        }
                    }
                    if (changed)
                        manager.Save();
                }
            }

            // Fix some cultivar commands.
            foreach (JObject cultivar in JsonUtilities.ChildrenRecursively(root, "Cultivar"))
            {
                if (!cultivar["Command"].HasValues)
                    continue;

                foreach (JValue command in cultivar["Command"].Children())
                {
                    foreach (var change in changedProperties)
                    {
                        string newName = change.Key;
                        string old = change.Value;
                        foreach (string modelName in modelNames)
                        {
                            command.Value = command.Value.ToString().Replace($"{modelName}.{old}", $"{modelName}.{newName}");
                            command.Value = command.Value.ToString().Replace($"[{modelName}].{old}", $"[{modelName}].{newName}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Upgrades to version 63. Rename the 'Water' node under soil to 'Physical'
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion63(JObject root, string fileName)
        {
            foreach (var water in JsonUtilities.ChildrenRecursively(root, "Water"))
            {
                water["$type"] = "Models.Soils.Physical, Models";
                JsonUtilities.RenameModel(water, "Physical");
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Water.", ".Physical.");
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "Factor"))
            {
                var specification = factor["Specification"];
                if (specification != null)
                {
                    var specificationString = specification.ToString();
                    specificationString = specificationString.Replace(".Water.", ".Physical.");
                    specificationString = specificationString.Replace("[Water]", "[Physical]");
                    factor["Specification"] = specificationString;
                }
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "CompositeFactor"))
            {
                var specifications = factor["Specifications"];
                if (specifications != null)
                {
                    for (int i = 0; i < specifications.Count(); i++)
                    {
                        var specificationString = specifications[i].ToString();
                        specificationString = specificationString.Replace(".Water.", ".Physical.");
                        specificationString = specificationString.Replace("[Water]", "[Physical]");
                        specifications[i] = specificationString;
                    }
                }
            }
        }


        /// <summary>
        /// Upgrades to version 64. Rename the 'SoilOrganicMatter' node under soil to 'Organic'
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion64(JObject root, string fileName)
        {
            foreach (var organic in JsonUtilities.ChildrenRecursively(root, "SoilOrganicMatter"))
            {
                organic["$type"] = "Models.Soils.Organic, Models";
                JsonUtilities.RenameModel(organic, "Organic");
                organic["FOMCNRatio"] = organic["RootCN"];
                organic["FOM"] = organic["RootWt"];
                organic["SoilCNRatio"] = organic["SoilCN"];
                organic["Carbon"] = organic["OC"];
                var ocUnits = organic["OCUnits"];
                if (ocUnits != null)
                {
                    string ocUnitsString = ocUnits.ToString();
                    if (ocUnitsString == "1" || ocUnitsString == "WalkleyBlack")
                    {
                        var oc = organic["Carbon"].Values<double>().ToArray();
                        oc = MathUtilities.Multiply_Value(oc, 1.3);
                        organic["Carbon"] = new JArray(oc);
                    }
                }
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilOrganicMatter.", ".Organic.");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".RootCN", ".FOMCNRatio");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".RootWt", ".FOM");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilCN", ".SoilCNRatio");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Organic.OC", ".Organic.Carbon");
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "Factor"))
            {
                var specification = factor["Specification"];
                if (specification != null)
                {
                    var specificationString = specification.ToString();
                    specificationString = specificationString.Replace(".SoilOrganicMatter.", ".Organic.");
                    specificationString = specificationString.Replace("[SoilOrganicMatter]", "[Organic]");
                    specificationString = specificationString.Replace(".Organic.OC", ".Organic.Carbon");
                    specificationString = specificationString.Replace(".RootCN", ".FOMCNRatio");
                    specificationString = specificationString.Replace(".RootWt", ".FOM");
                    specificationString = specificationString.Replace(".SoilCN", ".SoilCNRatio");
                    factor["Specification"] = specificationString;
                }
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "CompositeFactor"))
            {
                var specifications = factor["Specifications"];
                if (specifications != null)
                {
                    for (int i = 0; i < specifications.Count(); i++)
                    {
                        var specificationString = specifications[i].ToString();
                        specificationString = specificationString.Replace(".SoilOrganicMatter.", ".Organic.");
                        specificationString = specificationString.Replace("[SoilOrganicMatter]", "[Organic]");
                        specificationString = specificationString.Replace(".OC", ".Carbon");
                        specificationString = specificationString.Replace(".RootCN", ".FOMCNRatio");
                        specificationString = specificationString.Replace(".RootWt", ".FOM");
                        specificationString = specificationString.Replace(".SoilCN", ".SoilCNRatio");
                        specifications[i] = specificationString;
                    }
                }
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilOrganicMatter", "Organic");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace(".Organic.OC", ".Organic.Carbon");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilOrganicMatter", "Organic");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace(".Organic.OC", ".Organic.Carbon");
                }
            }

            foreach (var child in JsonUtilities.ChildrenRecursively(root))
            {
                if (JsonUtilities.Type(child) == "Morris" || JsonUtilities.Type(child) == "Sobol")
                {
                    var parameters = child["Parameters"];
                    for (int i = 0; i < parameters.Count(); i++)
                    {
                        var parameterString = parameters[i]["Path"].ToString();
                        parameterString = parameterString.Replace(".SoilOrganicMatter.", ".Organic.");
                        parameterString = parameterString.Replace("[SoilOrganicMatter]", "[Organic]");
                        parameterString = parameterString.Replace(".OC", ".Carbon");
                        parameters[i]["Path"] = parameterString;
                    }
                }
            }
        }

        /// <summary>
        /// Upgrades to version 65. Rename the 'Analysis' node under soil to 'Chemical'
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion65(JObject root, string fileName)
        {
            foreach (var chemical in JsonUtilities.ChildrenRecursively(root, "Analysis"))
            {
                var soil = JsonUtilities.Parent(chemical);
                var physical = JsonUtilities.ChildWithName(soil as JObject, "Physical");

                chemical["$type"] = "Models.Soils.Chemical, Models";
                JsonUtilities.RenameModel(chemical, "Chemical");
                if (physical != null && physical["Thickness"] != null)
                {
                    // Move particle size numbers from chemical to physical and make sure layers are mapped.
                    var physicalThickness = physical["Thickness"].Values<double>().ToArray();
                    var chemicalThickness = chemical["Thickness"].Values<double>().ToArray();

                    if (chemical["ParticleSizeSand"] != null && chemical["ParticleSizeSand"].HasValues)
                    {
                        var values = chemical["ParticleSizeSand"].Values<double>().ToArray();
                        if (values.Length < physicalThickness.Length)
                            Array.Resize(ref values, chemicalThickness.Length);
                        var mappedValues = Soils.Standardiser.Layers.MapConcentration(values, chemicalThickness, physicalThickness, values.Last());
                        physical["ParticleSizeSand"] = new JArray(mappedValues);
                    }

                    if (chemical["ParticleSizeSilt"] != null && chemical["ParticleSizeSilt"].HasValues)
                    {
                        var values = chemical["ParticleSizeSilt"].Values<double>().ToArray();
                        if (values.Length < physicalThickness.Length)
                            Array.Resize(ref values, chemicalThickness.Length);
                        var mappedValues = Soils.Standardiser.Layers.MapConcentration(values, chemicalThickness, physicalThickness, values.Last());
                        physical["ParticleSizeSilt"] = new JArray(mappedValues);
                    }

                    if (chemical["ParticleSizeClay"] != null && chemical["ParticleSizeClay"].HasValues)
                    {
                        var values = chemical["ParticleSizeClay"].Values<double>().ToArray();
                        if (values.Length < physicalThickness.Length)
                            Array.Resize(ref values, chemicalThickness.Length);
                        var mappedValues = Soils.Standardiser.Layers.MapConcentration(values, chemicalThickness, physicalThickness, values.Last());
                        physical["ParticleSizeClay"] = new JArray(mappedValues);
                    }

                    // convert ph units
                    var phUnits = physical["PHUnits"];
                    if (phUnits != null)
                    {
                        string phUnitsString = phUnits.ToString();
                        if (phUnitsString == "1")
                        {
                            // pH in water = (pH in CaCl X 1.1045) - 0.1375
                            var ph = physical["PH"].Values<double>().ToArray();
                            ph = MathUtilities.Subtract_Value(MathUtilities.Multiply_Value(ph, 1.1045), 0.1375);
                            chemical["PH"] = new JArray(ph);
                        }
                    }
                }
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Analysis.", ".Chemical.");
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "Factor"))
            {
                var specification = factor["Specification"];
                if (specification != null)
                {
                    var specificationString = specification.ToString();
                    specificationString = specificationString.Replace(".Analysis.", ".Chemical.");
                    specificationString = specificationString.Replace("[Analysis]", "[Chemical]");
                    factor["Specification"] = specificationString;
                }
            }

            foreach (var factor in JsonUtilities.ChildrenOfType(root, "CompositeFactor"))
            {
                var specifications = factor["Specifications"];
                if (specifications != null)
                {
                    for (int i = 0; i < specifications.Count(); i++)
                    {
                        var specificationString = specifications[i].ToString();
                        specificationString = specificationString.Replace(".Analysis.", ".Chemical.");
                        specificationString = specificationString.Replace("[Analysis]", "[Chemical]");
                        specifications[i] = specificationString;
                    }
                }
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("Analysis", "Chemical");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("Analysis", "Chemical");
                }
            }

            foreach (var child in JsonUtilities.ChildrenRecursively(root))
            {
                if (JsonUtilities.Type(child) == "Morris" || JsonUtilities.Type(child) == "Sobol")
                {
                    var parameters = child["Parameters"];
                    for (int i = 0; i < parameters.Count(); i++)
                    {
                        var parameterString = parameters[i]["Path"].ToString();
                        parameterString = parameterString.Replace(".Analysis.", ".Chemical.");
                        parameterString = parameterString.Replace("[Analysis]", "[Chemical]");
                        parameters[i]["Path"] = parameterString;
                    }
                }
            }
        }

        /// <summary>
        /// When a factor is under a factors model, insert a permutation model.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion66(JToken root, string fileName)
        {
            foreach (var factors in JsonUtilities.ChildrenRecursively(root as JObject, "Factors"))
            {
                if (JsonUtilities.Children(factors).Count > 1)
                {
                    var permutationsNode = new JObject();
                    permutationsNode["$type"] = "Models.Factorial.Permutation, Models";
                    JsonUtilities.RenameModel(permutationsNode, "Permutation");
                    permutationsNode["Children"] = factors["Children"];
                    var children = new JArray(permutationsNode);
                    factors["Children"] = children;
                }
            }
        }

        /// <summary>
        /// Upgrades to version 67. Sets the Start and End properties
        /// in clock to the values previously stored in StartDate and EndDate.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion67(JObject root, string fileName)
        {
            foreach (JObject clock in JsonUtilities.ChildrenRecursively(root, "Clock"))
            {
                clock["Start"] = clock["StartDate"];
                clock["End"] = clock["EndDate"];
            }
        }

        /// <summary>
        /// Upgrades to version 68. Removes AgPasture.Sward
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion68(JObject root, string fileName)
        {
            foreach (JObject sward in JsonUtilities.ChildrenRecursively(root, "Sward"))
            {
                foreach (JObject pastureSpecies in JsonUtilities.Children(sward))
                {
                    if (pastureSpecies["Name"].ToString().Equals("Ryegrass", StringComparison.InvariantCultureIgnoreCase))
                        JsonUtilities.RenameModel(pastureSpecies, "AGPRyegrass");
                    if (pastureSpecies["Name"].ToString().Equals("WhiteClover", StringComparison.InvariantCultureIgnoreCase))
                        JsonUtilities.RenameModel(pastureSpecies, "AGPWhiteClover");
                    pastureSpecies["ResourceName"] = pastureSpecies["Name"];

                    var swardParentChildren = JsonUtilities.Parent(sward)["Children"] as JArray;
                    swardParentChildren.Add(pastureSpecies);
                }
                sward.Remove();
            }

            bool foundAgPastureWhiteClover = false; // as opposed to a PMF whiteclover
            foreach (JObject pastureSpecies in JsonUtilities.ChildrenRecursively(root, "PastureSpecies"))
            {
                if (pastureSpecies["Name"].ToString().Equals("Ryegrass", StringComparison.InvariantCultureIgnoreCase))
                    JsonUtilities.RenameModel(pastureSpecies, "AGPRyegrass");
                if (pastureSpecies["Name"].ToString().Equals("WhiteClover", StringComparison.InvariantCultureIgnoreCase))
                {
                    JsonUtilities.RenameModel(pastureSpecies, "AGPWhiteClover");
                    foundAgPastureWhiteClover = true;
                }

                pastureSpecies["ResourceName"] = pastureSpecies["Name"];
            }

            foreach (JObject soilCrop in JsonUtilities.ChildrenRecursively(root, "SoilCrop"))
            {
                if (soilCrop["Name"].ToString().Equals("SwardSoil", StringComparison.InvariantCultureIgnoreCase))
                    soilCrop.Remove();
                if (soilCrop["Name"].ToString().Equals("RyegrassSoil", StringComparison.InvariantCultureIgnoreCase))
                    JsonUtilities.RenameModel(soilCrop, "AGPRyegrassSoil");
                if (foundAgPastureWhiteClover && soilCrop["Name"].ToString().Equals("WhiteCloverSoil", StringComparison.InvariantCultureIgnoreCase))
                    JsonUtilities.RenameModel(soilCrop, "AGPWhiteCloverSoil");
            }
        }

        /// <summary>
        /// Upgrades to version 69. Fixes link attributes in manager scripts after
        /// link refactoring.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the .apsimx file.</param>
        private static void UpgradeToVersion69(JObject root, string fileName)
        {
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                // The linking code previously had a hack which automatically enabled link by name if the target
                // model type is IFunction or Biomass (or any inherited class thereof). I've removed this hack 
                // from the link resolution code, which means that all such links must be adjusted accordingly.

                // [Link(...)] [Units] [...] Biomass -> [Link(ByName = true, ...)] [Units] [...] Biomass
                manager.ReplaceRegex(@"\[Link\(([^\)]+)\)\]((\s*\[[^\]]+\])*\s*(public|private|protected|internal|static|readonly| )*\s*(IFunction|Biomass|CNReductionForCover|CNReductionForTillage|RunoffModel|WaterTableModel|HeightFunction|DecumulateFunction|EndOfDayFunction|LiveOnEventFunction|AccumulateAtEvent|DailyMeanVPD|CERESDenitrificationWaterFactor|CERESDenitrificationTemperatureFactor|CERESMineralisationFOMCNRFactor|DayCentN2OFractionModel|CERESNitrificationpHFactor|CERESNitrificationWaterFactor|CERESUreaHydrolysisModel|CERESMineralisationWaterFactor|CERESMineralisationTemperatureFactor|CERESNitrificationModel|StringComparisonFunction|AccumulateByDate|AccumulateByNumericPhase|TrackerFunction|ArrayFunction|WangEngelTempFunction|BoundFunction|LinearAfterThresholdFunction|SoilWaterScale|MovingAverageFunction|HoldFunction|DeltaFunction|MovingSumFunction|QualitativePPEffect|AccumulateFunction|AddFunction|AgeCalculatorFunction|AirTemperatureFunction|BellCurveFunction|Constant|DivideFunction|ExponentialFunction|ExpressionFunction|ExternalVariable|LessThanFunction|LinearInterpolationFunction|MaximumFunction|MinimumFunction|MultiplyFunction|OnEventFunction|PhaseBasedSwitch|PhaseLookup|PhaseLookupValue|PhotoperiodDeltaFunction|PhotoperiodFunction|PowerFunction|SigmoidFunction|SoilTemperatureDepthFunction|SoilTemperatureFunction|SoilTemperatureWeightedFunction|SplineInterpolationFunction|StageBasedInterpolation|SubtractFunction|VariableReference|WeightedTemperatureFunction|XYPairs|CanopyPhotosynthesis|RUECO2Function|RUEModel|StorageDMDemandFunction|StorageNDemandFunction|InternodeCohortDemandFunction|BerryFillingRateFunction|TEWaterDemandFunction|FillingRateFunction|AllometricDemandFunction|InternodeDemandFunction|PartitionFractionDemandFunction|PopulationBasedDemandFunction|PotentialSizeDemandFunction|RelativeGrowthRateDemandFunction))", @"[Link(Type = LinkType.Child, ByName = true, $1)]$2");

                // [Link] IFunction -> [Link(ByName = true)] IFunction
                manager.ReplaceRegex(@"\[Link\]((\s*\[[^\]]+\])*\s*(public|private|protected|internal|static|readonly| )*\s*(IFunction|Biomass|CNReductionForCover|CNReductionForTillage|RunoffModel|WaterTableModel|HeightFunction|DecumulateFunction|EndOfDayFunction|LiveOnEventFunction|AccumulateAtEvent|DailyMeanVPD|CERESDenitrificationWaterFactor|CERESDenitrificationTemperatureFactor|CERESMineralisationFOMCNRFactor|DayCentN2OFractionModel|CERESNitrificationpHFactor|CERESNitrificationWaterFactor|CERESUreaHydrolysisModel|CERESMineralisationWaterFactor|CERESMineralisationTemperatureFactor|CERESNitrificationModel|StringComparisonFunction|AccumulateByDate|AccumulateByNumericPhase|TrackerFunction|ArrayFunction|WangEngelTempFunction|BoundFunction|LinearAfterThresholdFunction|SoilWaterScale|MovingAverageFunction|HoldFunction|DeltaFunction|MovingSumFunction|QualitativePPEffect|AccumulateFunction|AddFunction|AgeCalculatorFunction|AirTemperatureFunction|BellCurveFunction|Constant|DivideFunction|ExponentialFunction|ExpressionFunction|ExternalVariable|LessThanFunction|LinearInterpolationFunction|MaximumFunction|MinimumFunction|MultiplyFunction|OnEventFunction|PhaseBasedSwitch|PhaseLookup|PhaseLookupValue|PhotoperiodDeltaFunction|PhotoperiodFunction|PowerFunction|SigmoidFunction|SoilTemperatureDepthFunction|SoilTemperatureFunction|SoilTemperatureWeightedFunction|SplineInterpolationFunction|StageBasedInterpolation|SubtractFunction|VariableReference|WeightedTemperatureFunction|XYPairs|CanopyPhotosynthesis|RUECO2Function|RUEModel|StorageDMDemandFunction|StorageNDemandFunction|InternodeCohortDemandFunction|BerryFillingRateFunction|TEWaterDemandFunction|FillingRateFunction|AllometricDemandFunction|InternodeDemandFunction|PartitionFractionDemandFunction|PopulationBasedDemandFunction|PotentialSizeDemandFunction|RelativeGrowthRateDemandFunction))", @"[Link(Type = LinkType.Child, ByName = true)]$1");

                // Here I assume that all [LinkByPath] links will have a path argument supplied.
                // [LinkByPath(...)] -> [Link(Type = LinkType.Path, ...)]
                manager.ReplaceRegex(@"\[LinkByPath\(([^\)]+)\)", @"[Link(Type = LinkType.Path, $1)");

                // [ParentLink(...)] -> [Link(Type = LinkType.Ancestor, ...)]
                manager.ReplaceRegex(@"\[ParentLink\(([^\)]+)\)", @"[Link(Type = LinkType.Ancestor, $1)");

                // [ParentLink] -> [Link(Type = LinkType.Ancestor, ByName = false)]
                manager.Replace("[ParentLink]", "[Link(Type = LinkType.Ancestor)]", caseSensitive: true);

                // [ScopedLinkByName(...)] -> [Link(ByName = true, ...)]
                manager.ReplaceRegex(@"\[ScopedLinkByName\(([^\)]+)\)", @"[Link(ByName = true, $1)");

                // [ScopedLinkByName] -> [Link(ByName = true)]
                manager.Replace("[ScopedLinkByName]", "[Link(ByName = true)]", caseSensitive: true);

                // [ScopedLink(...)] -> [Link(...)]
                manager.ReplaceRegex(@"\[ScopedLink\(([^\)]+)\)", @"[Link($1)");

                // [ScopedLink] -> [Link]
                manager.Replace("[ScopedLink]", "[Link]", caseSensitive: true);

                // [ChildLinkByName(...)] -> [Link(Type = LinkType.Child, ByName = true, ...)]
                manager.ReplaceRegex(@"\[ChildLinkByName\(([^\)]+)\)", @"[Link(Type = LinkType.Child, ByName = true, $1)");

                // [ChildLinkByName] -> [Link(Type = LinkType.Child, ByName = true)]
                manager.Replace("[ChildLinkByName]", "[Link(Type = LinkType.Child, ByName = true)]", caseSensitive: true);

                // [ChildLink(...)] -> [Link(Type = LinkType.Child, ...)]
                manager.ReplaceRegex(@"\[ChildLink\(([^\)]+)\)", @"[Link(Type = LinkType.Child, $1)");

                // [ChildLink] -> [Link(Type = LinkType.Child)]
                manager.Replace("[ChildLink]", "[Link(Type = LinkType.Child)]", caseSensitive: true);

                manager.Save();
            }
        }

        /// <summary>
        /// Changes the type of the Stock component inital values genotypes array
        /// from StockGeno to SingleGenotypeInits.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion70(JObject root, string fileName)
        {
            foreach (var stockNode in JsonUtilities.ChildrenOfType(root, "Stock"))
            {
                // for each element of GenoTypes[]
                var genotypes = stockNode["GenoTypes"];
                for (int i = 0; i < genotypes.Count(); i++)
                {
                    genotypes[i]["$type"] = "Models.GrazPlan.SingleGenotypeInits, Models";
                    double dr = Convert.ToDouble(genotypes[i]["DeathRate"]);
                    double drw = Convert.ToDouble(genotypes[i]["WnrDeathRate"]);
                    genotypes[i]["DeathRate"] = new JArray(new double[] { dr, drw });
                    genotypes[i]["PotFleeceWt"] = genotypes[i]["RefFleeceWt"];
                    genotypes[i]["Conceptions"] = genotypes[i]["Conception"];
                    genotypes[i]["GenotypeName"] = genotypes[i]["Name"];
                }
            }
        }

        /// <summary>
        /// Alters all existing linint functions to have a child variable reference IFunction called XValue instead of a
        /// string property called XProperty that IFunction then had to locate
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion71(JObject root, string fileName)
        {
            foreach (JObject linint in JsonUtilities.ChildrenRecursively(root, "LinearInterpolationFunction"))
            {
                VariableReference varRef = new VariableReference();
                varRef.Name = "XValue";
                varRef.VariableName = linint["XProperty"].ToString();
                JsonUtilities.AddModel(linint, varRef);
                linint.Remove("XProperty");
            }
        }

        /// <summary>
        /// Remove .Value() from all variable references because it is redundant
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion72(JObject root, string fileName)
        {
            foreach (var varRef in JsonUtilities.ChildrenRecursively(root, "VariableReference"))
                varRef["VariableName"] = varRef["VariableName"].ToString().Replace(".Value()", "");

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
                JsonUtilities.SearchReplaceReportVariableNames(report, ".Value()", "");

            foreach (var graph in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (graph["XFieldName"] != null)
                    graph["XFieldName"] = graph["XFieldName"].ToString().Replace(".Value()", "");
                if (graph["X2FieldName"] != null)
                    graph["X2FieldName"] = graph["X2FieldName"].ToString().Replace(".Value()", "");
                if (graph["YFieldName"] != null)
                    graph["YFieldName"] = graph["YFieldName"].ToString().Replace(".Value()", "");
                if (graph["Y2FieldName"] != null)
                    graph["Y2FieldName"] = graph["Y2FieldName"].ToString().Replace(".Value()", "");
            }
        }

        /// <summary>
        /// Alters all existing AllometricDemand functions to have a child variable reference IFunction called XValue and YValue instead of 
        /// string property called XProperty and YProperty that it then had to locate.  Aiming to get all things using get for properties
        /// to be doing it via Variable reference so we can stream line scoping rules
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion73(JObject root, string fileName)
        {
            foreach (JObject Alomet in JsonUtilities.ChildrenRecursively(root, "AllometricDemandFunction"))
            {
                VariableReference XvarRef = new VariableReference();
                XvarRef.Name = "XValue";
                XvarRef.VariableName = Alomet["XProperty"].ToString();
                JsonUtilities.AddModel(Alomet, XvarRef);
                Alomet.Remove("XProperty");
                VariableReference YvarRef = new VariableReference();
                YvarRef.Name = "YValue";
                YvarRef.VariableName = Alomet["YProperty"].ToString();
                JsonUtilities.AddModel(Alomet, YvarRef);
                Alomet.Remove("YProperty");

            }
        }

        /// <summary>
        /// Changes the Surface Organic Matter property FractionFaecesAdded to 1.0
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion74(JObject root, string fileName)
        {
            foreach (JObject som in JsonUtilities.ChildrenRecursively(root, "SurfaceOrganicMatter"))
                som["FractionFaecesAdded"] = "1.0";
        }

        /// <summary>
        /// Change TreeLeafAreas to ShadeModiers in Tree Proxy
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion75(JObject root, string fileName)
        {
            foreach (JObject TreeProxy in JsonUtilities.ChildrenRecursively(root, "TreeProxy"))
            {
                TreeProxy["ShadeModifiers"] = TreeProxy["TreeLeafAreas"];
                // ShadeModifiers is sometimes null (not sure why) so fill it with 1s using Heights to get array length
                var SM = TreeProxy["Heights"].Values<double>().ToArray();
                for (int i = 0; i < SM.Count(); i++)
                    SM[i] = 1.0;
                TreeProxy["ShadeModifiers"] = new JArray(SM);
            }
        }

        /// <summary>
        /// Change flow_urea to FlowUrea in soil water
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion76(JObject root, string fileName)
        {
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                if (manager.Replace(".flow_urea", ".FlowUrea"))
                    manager.Save();
            }
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, ".flow_urea", ".FlowUrea");
            }
        }

        /// <summary>
        /// Change the property in Stock to Genotypes
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion77(JObject root, string fileName)
        {
            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                if (manager.Replace(".GenoTypes", ".Genotypes"))
                    manager.Save();
            }
            foreach (var stock in JsonUtilities.ChildrenOfType(root, "Stock"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(stock, ".GenoTypes", ".Genotypes");
            }
        }

        /// <summary>
        /// Change the namespace for SimpleGrazing
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion78(JObject root, string fileName)
        {
            foreach (var simpleGrazing in JsonUtilities.ChildrenOfType(root, "SimpleGrazing"))
            {
                simpleGrazing["$type"] = "Models.AgPasture.SimpleGrazing, Models";
            }
        }

        /// <summary>
        /// Change manager method and AgPasture variable names.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion79(JObject root, string fileName)
        {
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".Graze(", ".RemoveBiomass("),
                new Tuple<string, string>(".EmergingTissuesWt",   ".EmergingTissue.Wt"),
                new Tuple<string, string>(".EmergingTissuesN",    ".EmergingTissue.N"),
                new Tuple<string, string>(".DevelopingTissuesWt", ".DevelopingTissue.Wt"),
                new Tuple<string, string>(".DevelopingTissuesN",  ".DevelopingTissue.N"),
                new Tuple<string, string>(".MatureTissuesWt", ".MatureTissue.Wt"),
                new Tuple<string, string>(".MatureTissuesN",  ".MatureTissue.N"),
                new Tuple<string, string>(".DeadTissuesWt", ".DeadTissue.Wt"),
                new Tuple<string, string>(".DeadTissuesN",  ".DeadTissue.N")
            };

            JsonUtilities.RenameVariables(root, changes);
        }

        /// <summary>
        /// Replace ExcelMultiInput with ExcelInput.
        /// Change ExcelInput.FileName from a string into a string[].
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion80(JObject root, string fileName)
        {
            // Rename ExcelInput.FileName to FileNames and make it an array.
            foreach (JObject excelInput in JsonUtilities.ChildrenRecursively(root, "ExcelInput"))
            {
                if (excelInput["FileName"] != null)
                    excelInput["FileNames"] = new JArray(excelInput["FileName"].Value<string>());
            }

            // Replace ExcelMultiInput with an ExcelInput.
            foreach (JObject excelMultiInput in JsonUtilities.ChildrenRecursively(root, "ExcelMultiInput"))
            {
                excelMultiInput["$type"] = "Models.PostSimulationTools.ExcelInput, Models";
            }
        }


        /// <summary>
        /// Seperate life cycle process class into Growth, Transfer and Mortality classes.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion81(JObject root, string fileName)
        {
            // Rename ExcelInput.FileName to FileNames and make it an array.
            foreach (JObject LSP in JsonUtilities.ChildrenRecursively(root, "LifeStageProcess"))
            {
                if (int.Parse(LSP["ProcessAction"].ToString()) == 0) //Process is Transfer
                {
                    LSP["$type"] = "Models.LifeCycle.LifeStageTransfer, Models";
                }
                else if (int.Parse(LSP["ProcessAction"].ToString()) == 1) //Process is PhysiologicalGrowth
                {
                    LSP["$type"] = "Models.LifeCycle.LifeStageGrowth, Models";
                }
                else if (int.Parse(LSP["ProcessAction"].ToString()) == 2) //Process is Mortality
                {
                    LSP["$type"] = "Models.LifeCycle.LifeStageMortality, Models";
                }

            }

            foreach (JObject LSRP in JsonUtilities.ChildrenRecursively(root, "LifeStageReproductionProcess"))
            {
                LSRP["$type"] = "Models.LifeCycle.LifeStageReproduction, Models";
            }

            foreach (JObject LSIP in JsonUtilities.ChildrenRecursively(root, "LifeStageImmigrationProcess"))
            {
                LSIP["$type"] = "Models.LifeCycle.LifeStageImmigration, Models";
            }
        }

        /// <summary>
        /// Add Critical N Conc (if not existing) to all Root Objects by copying the maximum N conc
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion82(JObject root, string fileName)
        {
            foreach (JObject r in JsonUtilities.ChildrenRecursively(root, "Root"))
            {
                if (JsonUtilities.ChildWithName(r, "CriticalNConc") == null)
                {
                    JObject minNConc = JsonUtilities.ChildWithName(r, "MinimumNConc");
                    if (minNConc == null)
                        throw new Exception("Root has no CriticalNConc or MaximumNConc");

                    VariableReference varRef = new VariableReference();
                    varRef.Name = "CriticalNConc";
                    varRef.VariableName = "[Root].MinimumNConc";
                    JsonUtilities.AddModel(r, varRef);
                }
            }
        }

        /// <summary>
        /// Remove .Value() from everywhere possible.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion83(JObject root, string fileName)
        {
            // 1. Report
            foreach (JObject report in JsonUtilities.ChildrenRecursively(root, "Report"))
            {
                JArray variables = report["VariableNames"] as JArray;
                if (variables == null)
                    continue;

                for (int i = 0; i < variables.Count; i++)
                    variables[i] = variables[i].ToString().Replace(".Value()", "");
            }

            // 2. ExpressionFunction
            foreach (JObject function in JsonUtilities.ChildrenRecursively(root, "ExpressionFunction"))
                function["Expression"] = function["Expression"].ToString().Replace(".Value()", "");
        }

        /// <summary>
        /// Renames the Input.FileName property to FileNames and makes it an array.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion84(JObject root, string fileName)
        {
            foreach (JObject input in JsonUtilities.ChildrenRecursively(root, "Input"))
                if (input["FileName"] != null)
                    input["FileNames"] = new JArray(input["FileName"]);
        }

        /// <summary>
        /// Add a field to the Checkpoints table.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion85(JObject root, string fileName)
        {
            SQLite db = new SQLite();
            var dbFileName = Path.ChangeExtension(fileName, ".db");
            if (File.Exists(dbFileName))
            {
                db.OpenDatabase(dbFileName, false);
                if (db.TableExists("_Checkpoints"))
                {
                    if (!db.GetTableColumns("_Checkpoints").Contains("OnGraphs"))
                    {
                        db.AddColumn("_Checkpoints", "OnGraphs", "integer");
                    }
                }
            }
        }


        /// <summary>
        /// Add new methods structure to OrganArbitrator.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion86(JObject root, string fileName)
        {
            foreach (var arbitrator in JsonUtilities.ChildrenOfType(root, "OrganArbitrator"))
            {
                //remove DMArbitrator, and NArbitrator
                var children = JsonUtilities.Children(arbitrator);
                var exdm = children.Find(c => JsonUtilities.Name(c).Equals("dmArbitrator", StringComparison.OrdinalIgnoreCase));
                JsonUtilities.RemoveChild(arbitrator, JsonUtilities.Name(exdm));

                var exn = children.Find(c => JsonUtilities.Name(c).Equals("nArbitrator", StringComparison.OrdinalIgnoreCase));
                JsonUtilities.RemoveChild(arbitrator, JsonUtilities.Name(exn));

                JsonUtilities.RenameModel(exdm, "ArbitrationMethod");
                JsonUtilities.RenameModel(exn, "ArbitrationMethod");

                //Add DMArbitration
                var dm = JsonUtilities.CreateNewChildModel(arbitrator, "DMArbitration", "Models.PMF.BiomassTypeArbitrator");
                var folder = JsonUtilities.CreateNewChildModel(dm, "PotentialPartitioningMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "ReallocationMethod", "Models.PMF.Arbitrator.ReallocationMethod");
                JsonUtilities.CreateNewChildModel(folder, "AllocateFixationMethod", "Models.PMF.Arbitrator.AllocateFixationMethod");
                JsonUtilities.CreateNewChildModel(folder, "RetranslocationMethod", "Models.PMF.Arbitrator.RetranslocationMethod");
                JsonUtilities.CreateNewChildModel(folder, "SendPotentialDMAllocationsMethod", "Models.PMF.Arbitrator.SendPotentialDMAllocationsMethod");

                folder = JsonUtilities.CreateNewChildModel(dm, "AllocationMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "NutrientConstrainedAllocationMethod", "Models.PMF.Arbitrator.NutrientConstrainedAllocationMethod");
                JsonUtilities.CreateNewChildModel(folder, "DryMatterAllocationsMethod", "Models.PMF.Arbitrator.DryMatterAllocationsMethod");

                JArray dmChildren = dm["Children"] as JArray;
                dmChildren.Add(exdm);

                //Add N Arbitration
                var n = JsonUtilities.CreateNewChildModel(arbitrator, "NArbitration", "Models.PMF.BiomassTypeArbitrator");
                folder = JsonUtilities.CreateNewChildModel(n, "PotentialPartitioningMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "ReallocationMethod", "Models.PMF.Arbitrator.ReallocationMethod");

                folder = JsonUtilities.CreateNewChildModel(n, "ActualPartitioningMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "AllocateFixationMethod", "Models.PMF.Arbitrator.AllocateFixationMethod");
                JsonUtilities.CreateNewChildModel(folder, "RetranslocationMethod", "Models.PMF.Arbitrator.RetranslocationMethod");

                folder = JsonUtilities.CreateNewChildModel(n, "AllocationMethods", "Models.Core.Folder");
                JsonUtilities.CreateNewChildModel(folder, "NitrogenAllocationsMethod", "Models.PMF.Arbitrator.NitrogenAllocationsMethod");

                JArray nChildren = n["Children"] as JArray;
                nChildren.Add(exn);
                var allocatesMethod = JsonUtilities.CreateNewChildModel(n, "AllocateUptakesMethod", "Models.PMF.Arbitrator.AllocateUptakesMethod");

                var water = JsonUtilities.CreateNewChildModel(arbitrator, "WaterUptakeMethod", "Models.PMF.Arbitrator.WaterUptakeMethod");
                var nitrogen = JsonUtilities.CreateNewChildModel(arbitrator, "NitrogenUptakeMethod", "Models.PMF.Arbitrator.NitrogenUptakeMethod");
            }
        }

        /// <summary>
        /// Remove Models.Report namespace.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion87(JObject root, string fileName)
        {
            // Fix type of Report nodes
            foreach (JObject report in JsonUtilities.ChildrenRecursively(root, "Report"))
                report["$type"] = report["$type"].ToString().Replace("Report.Report", "Report");

            // Fix type of all models in the now-removed Models.Graph namespace
            foreach (JObject model in JsonUtilities.ChildrenRecursively(root))
                model["$type"] = model["$type"].ToString().Replace("Models.Graph.", "Models.");

            // Fix graph axes - these are a property of graphs, not a model themselves
            foreach (JObject graph in JsonUtilities.ChildrenRecursively(root, "Graph"))
            {
                JArray axes = graph["Axis"] as JArray;
                if (axes != null)
                    foreach (JObject axis in axes)
                        axis["$type"] = axis["$type"].ToString().Replace("Models.Graph", "Models");
            }

            // Fix nutrient directed graphs - the nodes/arcs are not children, but
            // need to have their types fixed.
            foreach (JObject nutrient in JsonUtilities.ChildrenRecursively(root, "Nutrient"))
            {
                JObject directedGraph = nutrient["DirectedGraphInfo"] as JObject;
                if (directedGraph != null)
                {
                    directedGraph["$type"] = directedGraph["$type"].ToString().Replace("Models.Graph.", "Models.");
                    JArray nodes = directedGraph["Nodes"] as JArray;
                    if (nodes != null)
                        foreach (JObject node in nodes)
                            node["$type"] = node["$type"].ToString().Replace("Models.Graph.", "Models.");
                    JArray arcs = directedGraph["Arcs"] as JArray;
                    if (arcs != null)
                        foreach (JObject arc in arcs)
                            arc["$type"] = arc["$type"].ToString().Replace("Models.Graph.", "Models.");
                }
            }

            // Replace ExcelMultiInput with an ExcelInput.
            foreach (ManagerConverter manager in JsonUtilities.ChildManagers(root))
            {
                manager.Replace("Models.Report", "Models");
                manager.Replace("using Report", "using Models");
                //manager.ReplaceRegex("(using Models.+)using Models", "$1");
                manager.Replace("Report.Report", "Report");

                manager.Replace("Models.Graph", "Models");
                manager.Replace("Graph.Graph", "Graph");

                List<string> usingStatements = manager.GetUsingStatements().ToList();
                usingStatements.Remove("Models.Graph");
                usingStatements.Remove("Graph");

                manager.SetUsingStatements(usingStatements.Distinct());

                manager.Save();
            }
        }

        /// <summary>
        /// Replace SoilWater model with WaterBalance model.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion88(JObject root, string fileName)
        {
            foreach (JObject soilWater in JsonUtilities.ChildrenRecursively(root, "SoilWater"))
            {
                soilWater["$type"] = "Models.WaterModel.WaterBalance, Models";
                soilWater["ResourceName"] = "WaterBalance";
                if (soilWater["discharge_width"] != null)
                    soilWater["DischargeWidth"] = soilWater["discharge_width"];
                if (soilWater["catchment_area"] != null)
                    soilWater["CatchmentArea"] = soilWater["catchment_area"];
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                bool managerChanged = false;

                var declarations = manager.GetDeclarations();
                foreach (var declaration in declarations)
                {
                    if (declaration.TypeName == "SoilWater")
                    {
                        declaration.TypeName = "ISoilWater";
                        managerChanged = true;
                    }
                }

                if (managerChanged)
                {
                    manager.SetDeclarations(declarations);

                    var usings = manager.GetUsingStatements().ToList();
                    if (!usings.Contains("Models.Interfaces"))
                    {
                        usings.Add("Models.Interfaces");
                        manager.SetUsingStatements(usings);
                    }
                }

                if (manager.Replace(" as SoilWater", ""))
                    managerChanged = true;
                if (manager.Replace("solute_flow_eff", "SoluteFlowEfficiency"))
                    managerChanged = true;
                if (manager.Replace("solute_flux_eff", "SoluteFluxEfficiency"))
                    managerChanged = true;
                if (manager.Replace("[EventSubscribe(\"Commencing\")", "[EventSubscribe(\"StartOfSimulation\")"))
                    managerChanged = true;

                if (managerChanged)
                    manager.Save();
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "solute_flow_eff", "SoluteFlowEfficiency");
                JsonUtilities.SearchReplaceReportVariableNames(report, "solute_flux_eff", "SoluteFluxEfficiency");
            }

        }

        /// <summary>
        /// Replace 'avg' with 'mean' in report variables.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion89(JObject root, string fileName)
        {
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
                JsonUtilities.SearchReplaceReportVariableNames(report, "avg of ", "mean of ");
        }

        /// <summary>
        /// Fixes a bug where a manager script's children were being serialized.
        /// When attempting to reopen the file, the script's type cannot be resolved.
        /// This converter will strip out all script children of managers under replacements.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion90(JObject root, string fileName)
        {
            foreach (JObject replacements in JsonUtilities.ChildrenRecursively(root, "Replacements"))
                foreach (JObject manager in JsonUtilities.ChildrenRecursively(root, "Manager"))
                    JsonUtilities.RemoveChild(manager, "Script");
        }

        /// <summary>
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion91(JObject root, string fileName)
        {
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".HarvestableWt",          ".Harvestable.Wt"),
                new Tuple<string, string>(".HarvestableN",           ".Harvestable.N"),
                new Tuple<string, string>(".StandingHerbageWt",      ".Standing.Wt"),
                new Tuple<string, string>(".StandingHerbageN",       ".Standing.N"),
                new Tuple<string, string>(".StandingHerbageNConc",   ".Standing.NConc"),
                new Tuple<string, string>(".StandingLiveHerbageWt",  ".StandingLive.Wt"),
                new Tuple<string, string>(".StandingLiveHerbageN",   ".StandingLive.N"),
                new Tuple<string, string>(".StandingDeadHerbageWt",  ".StandingDead.Wt"),
                new Tuple<string, string>(".StandingDeadHerbageN",   ".StandingDead.N"),
                new Tuple<string, string>(".HerbageDigestibility",   ".Standing.Digestibility"),
                new Tuple<string, string>(".RootDepthMaximum",       ".Root.RootDepthMaximum"),
                new Tuple<string, string>("[AGPRyeGrass].RootLengthDensity", "[AGPRyeGrass].Root.RootLengthDensity"),
                new Tuple<string, string>("[AGPWhiteClover].RootLengthDensity", "[AGPWhiteClover].Root.RootLengthDensity"),
                new Tuple<string, string>("[AGPLucerne].RootLengthDensity", "[AGPLucerne].Root.RootLengthDensity")
            };
            JsonUtilities.RenameVariables(root, changes);
        }

        /// <summary>
        /// Change names of a couple of parameters in SimpleGrazing.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion92(JObject root, string fileName)
        {
            foreach (JObject simpleGrazing in JsonUtilities.ChildrenRecursively(root, "SimpleGrazing"))
            {
                simpleGrazing["FractionExcretedNToDung"] = simpleGrazing["FractionOfBiomassToDung"];
                if (simpleGrazing["FractionNExportedInAnimal"] == null)
                    simpleGrazing["FractionNExportedInAnimal"] = 0.75;
            }

            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".AmountDungCReturned",  ".AmountDungWtReturned")
            };
            JsonUtilities.RenameVariables(root, changes);
        }

        /// <summary>
        /// In SimpleGrazin, Turn "Fraction of defoliated N leaving the system" into a fraction of defoliated N going to soil.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion93(JObject root, string fileName)
        {
            foreach (JObject simpleGrazing in JsonUtilities.ChildrenRecursively(root, "SimpleGrazing"))
            {
                if (simpleGrazing["FractionNExportedInAnimal"] != null)
                {
                    var fractionNExportedInAnimal = Convert.ToDouble(simpleGrazing["FractionNExportedInAnimal"].Value<double>());
                    simpleGrazing["FractionDefoliatedNToSoil"] = 1 - fractionNExportedInAnimal;

                }
                if (simpleGrazing["FractionExcretedNToDung"] != null)
                {
                    var fractionExcretedNToDung = simpleGrazing["FractionExcretedNToDung"] as JArray;
                    if (fractionExcretedNToDung.Count > 0)
                        simpleGrazing["CNRatioDung"] = "NaN";
                }
            }
        }

        /// <summary>
        /// Convert stock genotypes array into GenotypeCross child models.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion94(JObject root, string fileName)
        {
            foreach (JObject stock in JsonUtilities.ChildrenRecursively(root, "Stock"))
            {
                var oldGenotypes = stock["Genotypes"] as JArray;
                if (oldGenotypes != null)
                {
                    var newGenotypes = new JArray();
                    foreach (var oldgenotype in oldGenotypes)
                    {
                        var genotypeCross = new JObject();
                        genotypeCross["$type"] = "Models.GrazPlan.GenotypeCross, Models";
                        genotypeCross["Name"] = oldgenotype["GenotypeName"];
                        var oldgenotypeDeathRates = oldgenotype["DeathRate"] as JArray;
                        if (oldgenotypeDeathRates != null && oldgenotypeDeathRates.Count == 2)
                        {
                            genotypeCross["MatureDeathRate"] = oldgenotypeDeathRates[0];
                            genotypeCross["WeanerDeathRate"] = oldgenotypeDeathRates[1];
                        }
                        genotypeCross["Conception"] = oldgenotype["Conceptions"];

                        if (string.IsNullOrEmpty(oldgenotype["DamBreed"].ToString()))
                            genotypeCross["PureBredBreed"] = oldgenotype["GenotypeName"];
                        else if (string.IsNullOrEmpty(oldgenotype["SireBreed"].ToString()))
                            genotypeCross["PureBredBreed"] = oldgenotype["DamBreed"];
                        else
                            genotypeCross["DamBreed"] = oldgenotype["DamBreed"];
                        genotypeCross["SireBreed"] = oldgenotype["SireBreed"];
                        genotypeCross["Generation"] = oldgenotype["Generation"];
                        genotypeCross["SRW"] = oldgenotype["SRW"];
                        genotypeCross["PotFleeceWt"] = oldgenotype["PotFleeceWt"];
                        genotypeCross["MaxFibreDiam"] = oldgenotype["MaxFibreDiam"];
                        genotypeCross["FleeceYield"] = oldgenotype["FleeceYield"];
                        genotypeCross["PeakMilk"] = oldgenotype["PeakMilk"];
                        newGenotypes.Add(genotypeCross);
                    }
                    stock.Remove("Genotypes");
                    if (newGenotypes.Count > 0)
                        stock["Children"] = newGenotypes;
                }
            }
            Tuple<string, string>[] changes =
            {
                new Tuple<string, string>(".GenotypeNamesAll()",  ".Genotypes.Names.ToArray()")
            };
            if (JsonUtilities.RenameVariables(root, changes))
            {
                // The replacement is in a manager. Need to make sure that LINQ is added as a using
                // because the .ToArray() depends on it.
                foreach (var manager in JsonUtilities.ChildManagers(root))
                {
                    var usings = manager.GetUsingStatements().ToList();
                    if (usings.Find(u => u.Contains("System.Linq")) == null)
                    {
                        usings.Add("System.Linq");
                        manager.SetUsingStatements(usings);
                        manager.Save();
                    }
                }
            }
        }

        /// <summary>
        /// Change initialDM on Generic organ and root from a single value to a BiomassPoolType so each type can be sepcified
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion95(JObject root, string fileName)
        {
            // Remove initalDM model and replace with initialBiomass object
            foreach (string org in new List<string>() { "GenericOrgan", "Root" })
            {
                foreach (JObject O in JsonUtilities.ChildrenRecursively(root, org))
                {
                    if (O["Enabled"].ToString() == "True")
                    {
                        string initName = org == "GenericOrgan" ? "InitialWtFunction" : "InitialDM";
                        JObject InitialWt = JsonUtilities.CreateNewChildModel(O, "InitialWt", "Models.PMF.BiomassDemand");
                        JObject Structural = JsonUtilities.ChildWithName(O, initName).DeepClone() as JObject;
                        Structural["Name"] = "Structural";
                        JArray ChildFunctions = new JArray();
                        ChildFunctions.Add(Structural);
                        InitialWt["Children"] = ChildFunctions;
                        JsonUtilities.AddConstantFunctionIfNotExists(InitialWt, "Metabolic", "0.0");
                        JsonUtilities.AddConstantFunctionIfNotExists(InitialWt, "Storage", "0.0");
                        JsonUtilities.RemoveChild(O, initName);
                    }
                }
            }
            // Altermanager code where initial root wt is being set.
            foreach (var manager in JsonUtilities.ChildrenOfType(root, "Manager"))
            {
                string code = manager["Code"].ToString();
                string[] lines = code.Split('\n');
                bool ContainsZoneInitalDM = false;
                for (int i = 0; i < lines.Count(); i++)
                {
                    if (lines[i].Contains("Root.ZoneInitialDM.Add(") && (ContainsZoneInitalDM == false))
                    {
                        ContainsZoneInitalDM = true;
                        string InitialDM = lines[i].Split('(')[1].Replace(";", "").Replace(")", "").Replace("\r", "").Replace("\n", "");
                        string NewCode = "BiomassDemand InitialDM = new BiomassDemand();\r\n" +
                                         "Constant InitStruct = new Constant();\r\n" +
                                         "InitStruct.FixedValue = " + InitialDM + ";\r\n" +
                                         "InitialDM.Structural = InitStruct;\r\n" +
                                         "Constant InitMetab = new Constant();\r\n" +
                                         "InitMetab.FixedValue = 0;\r\n" +
                                         "InitialDM.Metabolic = InitMetab;\r\n" +
                                         "Constant InitStor = new Constant();\r\n" +
                                         "InitStor.FixedValue = 0;\r\n" +
                                         "InitialDM.Storage = InitStor;\r\n" +
                                         lines[i].Split('(')[0] + "(InitialDM);\r\n";
                        lines[i] = NewCode;
                    }
                    else if (lines[i].Contains("Root.ZoneInitialDM.Add("))
                    {
                        string InitialDM = lines[i].Split('(')[1].Replace(";", "").Replace(")", "").Replace("\r", "").Replace("\n", "");
                        lines[i] = "InitStruct.FixedValue = " + InitialDM + ";\r\n" +
                                    lines[i].Split('(')[0] + "(InitialDM);\r\n";
                    }
                }

                if (ContainsZoneInitalDM)
                {
                    string newCode = "using Models.Functions;\r\n";
                    foreach (string line in lines)
                    {
                        newCode += line + "\n";
                    }
                    manager["Code"] = newCode;
                }
            }
        }

        /// <summary>
        /// Add RootShape to all simulations.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion96(JObject root, string fileName)
        {
            foreach (JObject thisRoot in JsonUtilities.ChildrenRecursively(root, "Root"))
            {
                if (JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeCylindre").Count == 0 &&
                    JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeSemiCircle").Count == 0 &&
                    JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeSemiCircleSorghum").Count == 0 &&
                    JsonUtilities.ChildrenRecursively(thisRoot, "RootShapeSemiEllipse").Count == 0)
                {
                    JArray rootChildren = thisRoot["Children"] as JArray;
                    if (rootChildren != null && rootChildren.Count > 0)
                    {
                        JToken thisPlant = JsonUtilities.Parent(thisRoot);

                        JArray rootShapeChildren = new JArray();
                        string type;

                        if (thisPlant["CropType"].ToString() == "Sorghum")
                        {
                            type = "Models.Functions.RootShape.RootShapeSemiCircleSorghum, Models";
                        }
                        else if (thisPlant["CropType"].ToString() == "C4Maize")
                        {
                            type = "Models.Functions.RootShape.RootShapeSemiCircle, Models";
                        }
                        else
                        {
                            type = "Models.Functions.RootShape.RootShapeCylindre, Models";
                        }

                        JObject rootShape = new JObject
                        {
                            ["$type"] = type,
                            ["Name"] = "RootShape",
                            ["Children"] = rootShapeChildren
                        };
                        rootChildren.AddFirst(rootShape);
                    }
                }
            }
        }

        /// <summary>
        /// Add RootShape to all simulations.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion97(JObject root, string fileName)
        {
            foreach (JObject AirTempFunc in JsonUtilities.ChildrenOfType(root, "AirTemperatureFunction"))
            {
                JObject tempResponse = JsonUtilities.ChildWithName(AirTempFunc, "XYPairs");
                tempResponse["Name"] = "TemperatureResponse";
            }
        }

        /// <summary>
        /// Convert stock animalparamset
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion98(JObject root, string fileName)
        {
            foreach (JObject paramSet in JsonUtilities.ChildrenRecursively(root, "AnimalParamSet"))
            {
                paramSet["$type"] = "Models.GrazPlan.Genotype, Models";
            }
        }

        /// <summary>
        /// Add InterpolationMethod to AirTemperature Function.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion99(JObject root, string fileName)
        {
            foreach (JObject AirTempFunc in JsonUtilities.ChildrenOfType(root, "AirTemperatureFunction"))
            {
                AirTempFunc["agregationMethod"] = "0";
                JsonUtilities.AddModel(AirTempFunc, typeof(ThreeHourSin), "InterpolationMethod");
            }
        }

        /// <summary>
        /// Change SimpleGrazing.FractionDefoliatedNToSoil from a scalar to an array.
        /// </summary>
        /// <param name="root">Root node.</param>
        /// <param name="fileName">Path to the .apsimx file.</param>
        private static void UpgradeToVersion100(JObject root, string fileName)
        {
            foreach (var simpleGrazing in JsonUtilities.ChildrenOfType(root, "SimpleGrazing"))
            {
                if (simpleGrazing["FractionDefoliatedNToSoil"] != null)
                {
                    var arr = new JArray();
                    arr.Add(simpleGrazing["FractionDefoliatedNToSoil"].Value<double>());
                    simpleGrazing["FractionDefoliatedNToSoil"] = arr;
                }
            }
        }

        /// <summary>
        /// Add progeny destination phase and mortality function.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="fileName"></param>
        private static void UpgradeToVersion900(JObject root, string fileName)
        {
            foreach (JObject LC in JsonUtilities.ChildrenRecursively(root, "LifeCycle"))
            {
                foreach (JObject LP in JsonUtilities.ChildrenRecursively(LC, "LifeCyclePhase"))
                {
                    JsonUtilities.AddConstantFunctionIfNotExists(LP, "Migration", "0.0");
                    JObject ProgDest = JsonUtilities.CreateNewChildModel(LP, "ProgenyDestination", "Models.LifeCycle.ProgenyDestinationPhase");
                    ProgDest["NameOfLifeCycleForProgeny"] = LC["Name"].ToString();
                    ProgDest["NameOfPhaseForProgeny"] = LP["NameOfPhaseForProgeny"].ToString();
                }
            }
        }

        /// <summary>
        /// Refactor LifeCycle model
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion890(JObject root, string fileName)
        {
            //Method to convert LifeCycleProcesses to functions
            void ChangeToFunction(JObject LifePhase, string OldType, string NewName, string FunctType, string SubFunctType = "")
            {
                JArray children = LifePhase["Children"] as JArray;
                var Process = JsonUtilities.ChildrenOfType(LifePhase, OldType);
                if (Process.Count >= 1)
                {
                    bool FirstProc = true;
                    JObject funct = new JObject();
                    JArray ChildFunctions = new JArray();
                    foreach (var proc in Process)
                    {
                        if (FunctType == "Add")
                        {
                            if (FirstProc)
                            {
                                funct["$type"] = "Models.Functions.AddFunction, Models";
                                funct["Name"] = NewName;
                                FirstProc = false;
                            }
                            foreach (var c in proc["Children"])
                            {
                                if ((SubFunctType == "All") && (c["$type"].ToString() != "Models.Memo, Models"))
                                {
                                    JObject cld = new JObject();
                                    cld["$type"] = "Models.Functions.MultiplyFunction, Models";
                                    cld["Name"] = c["Name"].ToString();
                                    JObject Popn = new JObject();
                                    Popn["$type"] = "Models.Functions.VariableReference, Models";
                                    Popn["Name"] = "CohortPopulation";
                                    Popn["VariableName"] = "[" + LifePhase["Name"].ToString() + "].CurrentCohort.Population";
                                    JArray kids = new JArray();
                                    kids.Add(Popn);
                                    kids.Add(c);
                                    cld["Children"] = kids;
                                    ChildFunctions.Add(cld);
                                }
                                else
                                    ChildFunctions.Add(c);
                            }
                        }
                        else if (FunctType == "Multiply")
                        {
                            if (FirstProc)
                            {
                                funct["$type"] = "Models.Functions.MultiplyFunction, Models";
                                funct["Name"] = NewName;
                                JObject Popn = new JObject();
                                Popn["$type"] = "Models.Functions.VariableReference, Models";
                                Popn["Name"] = "CohortPopulation";
                                Popn["VariableName"] = "[" + LifePhase["Name"].ToString() + "].CurrentCohort.Population";
                                ChildFunctions.Add(Popn);
                                FirstProc = false;
                            }
                            if (SubFunctType == "All")
                            {
                                foreach (JObject kid in proc["Children"])
                                    ChildFunctions.Add(kid);
                            }
                            else if (SubFunctType == "Min")
                            {
                                JObject Min = new JObject();
                                Min["$type"] = "Models.Functions.MinimumFunction, Models";
                                Min["Name"] = "ProgenyRate";
                                Min["Children"] = proc["Children"];
                                ChildFunctions.Add(Min);
                            }
                        }
                        else
                            throw new Exception("Something got Funct up");

                        JsonUtilities.RemoveChild(LifePhase, proc["Name"].ToString());
                    }
                    funct["Children"] = ChildFunctions;
                    children.Add(funct);
                }
                else
                    JsonUtilities.AddConstantFunctionIfNotExists(LifePhase, NewName, "0.0");
            }

            // Method to add infestation object
            void AddInfestObject(string Name, JToken zone, string Org, string Phase, JArray ChildFunction, int typeIndex)
            {
                JObject Infest = JsonUtilities.CreateNewChildModel(zone, Name, "Models.LifeCycle.Infestation");
                Infest["TypeOfInfestation"] = typeIndex;
                Infest["InfestingOrganisumName"] = Org;
                Infest["InfestingPhaseName"] = Phase;
                Infest["ChronoAgeOfImmigrants"] = 0;
                Infest["PhysAgeOfImmigrants"] = 0.2;
                Infest["Children"] = ChildFunction;
            }

            foreach (JObject LC in JsonUtilities.ChildrenRecursively(root, "LifeCycle"))
            {
                List<string> ChildPhases = new List<string>();
                JToken zone = JsonUtilities.Parent(LC);

                foreach (JObject LP in JsonUtilities.ChildrenRecursively(LC, "LifeStage"))
                {
                    LP["$type"] = "Models.LifeCycle.LifeCyclePhase, Models";
                    var ReproductiveProcess = JsonUtilities.ChildrenOfType(LP, "LifeStageReproduction");
                    //Convert LifeCycleProcesses to functions
                    ChangeToFunction(LP, "LifeStageImmigration", "Immigration", "Add");
                    ChangeToFunction(LP, "LifeStageGrowth", "Development", "Add");
                    ChangeToFunction(LP, "LifeStageMortality", "Mortality", "Add", "All");
                    ChangeToFunction(LP, "LifeStageReproduction", "Reproduction", "Multiply", "Min");
                    ChangeToFunction(LP, "LifeStageTransfer", "Graduation", "Multiply", "All");

                    //If functionality present in Graduation, move to development and delete graduation
                    var Dev = JsonUtilities.ChildWithName(LP, "Development");
                    var Grad = JsonUtilities.ChildWithName(LP, "Graduation");
                    if (Dev["$type"].ToString() == "Models.Functions.Constant, Models") //There was no functionality in Development
                    {
                        JsonUtilities.RemoveChild(LP, "Development");
                        Grad["Name"] = "Development"; //Set Graduation function to be Development
                        JsonUtilities.RemoveChild(Grad, "CohortPopulation");
                    }
                    else
                        JsonUtilities.RemoveChild(LP, "Graduation");

                    //Fix variable references
                    foreach (JObject xv in JsonUtilities.ChildrenOfType(LP, "VariableReference"))
                    {
                        if (xv["VariableName"].ToString() == "[LifeCycle].CurrentLifeStage.CurrentCohort.PhenoAge")
                            xv["VariableName"] = "[" + LP["Name"] + "].CurrentCohort.ChronologicalAge";
                    }
                    foreach (JObject xv in JsonUtilities.ChildrenOfType(LP, "LinearAfterThresholdFunction"))
                    {
                        if (xv["XProperty"].ToString() == "[LifeCycle].CurrentLifeStage.CurrentCohort.PhenoAge")
                            xv["XProperty"] = "[" + LP["Name"] + "].CurrentCohort.ChronologicalAge";
                    }

                    if (ReproductiveProcess.Count >= 1)
                    {
                        LP["NameOfPhaseForProgeny"] = ReproductiveProcess[0]["TransferTo"].ToString();
                    }
                    ChildPhases.Add(LP["Name"].ToString());
                }

                //Move immigration function from life phase to infestation event
                foreach (JObject LP in JsonUtilities.ChildrenRecursively(LC, "LifeCyclePhase"))
                {
                    JObject NumberFunction = JsonUtilities.ChildWithName(LP, "Immigration");
                    NumberFunction["Name"] = "NumberOfImmigrants";
                    JArray ChildFunction = new JArray();
                    ChildFunction.Add(NumberFunction);
                    if ((NumberFunction["$type"].ToString() == "Models.Functions.Constant, Models") &&
                        (double.Parse(NumberFunction["FixedValue"].ToString()) == 0.0))
                    {
                        //Don't add immigration event, not needed
                    }
                    else
                    {
                        AddInfestObject("Ongoing Infestation " + LP["Name"], zone, LC["Name"].ToString(), LP["Name"].ToString(), ChildFunction, 3);
                    }
                    JsonUtilities.RemoveChild(LP, "NumberOfImmigrants");
                }

                // Add infestation phase for initial populations
                int cou = 0;
                foreach (double init in LC["InitialPopulation"])
                {
                    if (init > 0)
                    {
                        string Name = "Initial Infestation " + ChildPhases[cou];
                        JObject NumberFunction = new JObject();
                        NumberFunction["$type"] = "Models.Functions.Constant, Models";
                        NumberFunction["Name"] = "NumberOfImmigrants";
                        NumberFunction["FixedValue"] = init;
                        JArray ChildFunction = new JArray();
                        ChildFunction.Add(NumberFunction);
                        AddInfestObject(Name, zone, LC["Name"].ToString(), ChildPhases[cou], ChildFunction, 0);
                        cou += 1;
                    }
                }
            }
            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "Migrants", "Graduates");
                JsonUtilities.SearchReplaceReportVariableNames(report, "Mortality", "Mortalities");
                JsonUtilities.SearchReplaceReportVariableNames(report, "PhenologicalAge", "PhysiologicalAge");
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                manager.Replace("LifeCycle.LifeStage", "LifeCycle.LifeCyclePhase");
                manager.Save();
            }
        }

        /// <summary>
        /// Changes initial Root Wt to an array.
        /// </summary>
        /// <param name="root">The root JSON token.</param>
        /// <param name="fileName">The name of the apsimx file.</param>
        private static void UpgradeToVersion999(JObject root, string fileName)
        {
            // Delete all alias children.
            foreach (var soilNitrogen in JsonUtilities.ChildrenOfType(root, "SoilNitrogen"))
            {
                var parent = JsonUtilities.Parent(soilNitrogen);
                var nutrient = JsonUtilities.CreateNewChildModel(parent, "Nutrient", "Models.Soils.Nutrients.Nutrient");
                nutrient["ResourceName"] = "Nutrient";
                soilNitrogen.Remove();
            }

            foreach (var manager in JsonUtilities.ChildManagers(root))
            {
                manager.Replace("using Models.Soils;", "using Models.Soils;\r\nusing Models.Soils.Nutrients;");

                manager.Replace("SoilNitrogen.FOMN", ".Nutrient.FOMN");
                manager.Replace("SoilNitrogen.FOMC", ".Nutrient.FOMC");

                if (manager.Replace("Soil.SoilNitrogen.HumicN", "Humic.N"))
                    manager.AddDeclaration("NutrientPool", "Humic", new string[] { "[ScopedLinkByName]" });
                if (manager.Replace("Soil.SoilNitrogen.HumicC", "Humic.C"))
                    manager.AddDeclaration("NutrientPool", "Humic", new string[] { "[ScopedLinkByName]" });

                if (manager.Replace("Soil.SoilNitrogen.MicrobialN", "Microbial.N"))
                    manager.AddDeclaration("NutrientPool", "Microbial", new string[] { "[ScopedLinkByName]" });
                if (manager.Replace("Soil.SoilNitrogen.MicrobialC", "Microbial.C"))
                    manager.AddDeclaration("NutrientPool", "Microbial", new string[] { "[ScopedLinkByName]" });

                if (manager.Replace("Soil.SoilNitrogen.dlt_n_min_res", "SurfaceResidueDecomposition.MineralisedN"))
                    manager.AddDeclaration("CarbonFlow", "SurfaceResidueDecomposition", new string[] { "[LinkByPath(Path=\"[Nutrient].SurfaceResidue.Decomposition\")]" });

                manager.Replace("SoilNitrogen.MineralisedN", "Nutrient.MineralisedN");

                manager.Replace("SoilNitrogen.TotalN", "Nutrient.TotalN");
                if (manager.Replace("SoilNitrogen.TotalN", "Nutrient.TotalN"))
                {
                    manager.RemoveDeclaration("SoilNitrogen");
                    manager.AddDeclaration("INutrient", "Nutrient", new string[] { "[ScopedLinkByName]" });
                }

                manager.Replace("SoilNitrogen.TotalC", "Nutrient.TotalC");
                if (manager.Replace("SoilNitrogen.TotalC", "Nutrient.TotalC"))
                {
                    manager.RemoveDeclaration("SoilNitrogen");
                    manager.AddDeclaration("INutrient", "Nutrient", new string[] { "[ScopedLinkByName]" });
                }

                manager.Replace("SoilNitrogen.mineral_n", "Nutrient.MineralN");
                manager.Replace("SoilNitrogen.Denitrification", "Nutrient.Natm");
                manager.Replace("SoilNitrogen.n2o_atm", "Nutrient.N2Oatm");
                manager.Save();
            }

            foreach (var report in JsonUtilities.ChildrenOfType(root, "Report"))
            {
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NO3.kgha", "Nutrient.NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.NH4.kgha", "Nutrient.NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.Urea.kgha", "Nutrient.Urea.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.PlantAvailableNO3.kgha", "Nutrient.PlantAvailableNO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "SoilNitrogen.PlantAvailableNH4.kgha", "Nutrient.PlantAvailableNH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].NO3.kgha", "[Nutrient].NO3.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].NH4.kgha", "[Nutrient].NH4.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, "[SoilNitrogen].Urea.kgha", "[Nutrient].Urea.kgha");

                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.FOMN", ".Nutrient.FOMN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.FOMC", ".Nutrient.FOMC");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.HumicN", ".Nutrient.Humic.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.HumicC", ".Nutrient.Humic.C");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.MicrobialN", ".Nutrient.Microbial.N");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.MicrobialC", ".Nutrient.Microbial.C");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.urea", ".Nutrient.Urea.kgha");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.dlt_n_min_res", ".Nutrient.SurfaceResidue.Decomposition.MineralisedN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.MineralisedN", ".Nutrient.MineralisedN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Denitrification", ".Nutrient.Natm");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.n2o_atm", ".Nutrient.N2Oatm");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.TotalC", ".Nutrient.TotalC");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.TotalN", ".Nutrient.TotalN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.mineral_n", ".Nutrient.MineralN");
                JsonUtilities.SearchReplaceReportVariableNames(report, ".SoilNitrogen.Nitrification", ".Nutrient.NH4.Nitrification");
            }

            foreach (var series in JsonUtilities.ChildrenOfType(root, "Series"))
            {
                if (series["XFieldName"] != null)
                {
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NO3.kgha", "Nutrient.NO3.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.NH4.kgha", "Nutrient.NH4.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.Urea.kgha", "Nutrient.Urea.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNO3.kgha", "Nutrient.PlantAvailableNO3.kgha");
                    series["XFieldName"] = series["XFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNH4.kgha", "Nutrient.PlantAvailableNH4.kgha");
                }
                if (series["YFieldName"] != null)
                {
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NO3.kgha", "Nutrient.NO3.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.NH4.kgha", "Nutrient.NH4.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.Urea.kgha", "Nutrient.Urea.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNO3.kgha", "Nutrient.PlantAvailableNO3.kgha");
                    series["YFieldName"] = series["YFieldName"].ToString().Replace("SoilNitrogen.PlantAvailableNH4.kgha", "Nutrient.PlantAvailableNH4.kgha");
                }
            }

        }
    }
}

