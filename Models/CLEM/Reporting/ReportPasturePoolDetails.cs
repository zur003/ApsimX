﻿using Models.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Models;
using APSIM.Shared.Utilities;
using System.Data;
using System.IO;
using Models.CLEM.Resources;
using Models.Core.Attributes;
using Models.Core.Run;
using Models.Storage;
using System.Globalization;

namespace Models.CLEM.Reporting
{
    /// <summary>
    /// A report class for writing output to the data store.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.ReportView")]
    [PresenterName("UserInterface.Presenters.ReportPresenter")]
    [ValidParent(ParentType = typeof(ZoneCLEM))]
    [ValidParent(ParentType = typeof(CLEMFolder))]
    [ValidParent(ParentType = typeof(Folder))]
    [Description("This report automatically generates a current balance column for each CLEM Resource Type\nassociated with the CLEM Resource Groups specified (name only) in the variable list.")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Reporting/PasturePoolDetails.htm")]
    public class ReportPasturePoolDetails: Models.Report
    {
        [Link]
        private ResourcesHolder Resources = null;

        /// <summary>The columns to write to the data store.</summary>
        [NonSerialized]
        private List<IReportColumn> columns = null;
        [NonSerialized]
        private ReportData dataToWriteToDb = null;

        /// <summary>Link to a simulation</summary>
        [Link]
        private Simulation simulation = null;

        /// <summary>Link to a clock model.</summary>
        [Link]
        private IClock clock = null;

        /// <summary>Link to a storage service.</summary>
        [Link]
        private IDataStore storage = null;

        /// <summary>Link to a locator service.</summary>
        [Link]
        private ILocator locator = null;

        /// <summary>Link to an event service.</summary>
        [Link]
        private IEvent events = null;

        private GrazeFoodStoreType grazeStore;

        /// <summary>An event handler to allow us to initialize ourselves.</summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            dataToWriteToDb = null;
            // sanitise the variable names and remove duplicates
            IModel zone = Apsim.Parent(this, typeof(Zone));
            List<string> variableNames = new List<string>();
            if (VariableNames != null)
            {
                for (int i = 0; i < this.VariableNames.Length; i++)
                {
                    // each variable name is now a GrazeFoodStoreType
                    bool isDuplicate = StringUtilities.IndexOfCaseInsensitive(variableNames, this.VariableNames[i].Trim()) != -1;
                    if (!isDuplicate && this.VariableNames[i] != string.Empty)
                    {
                        if (this.VariableNames[i].StartsWith("["))
                        {
                            variableNames.Add(this.VariableNames[i]);
                        }
                        else
                        {
                            string[] splitName = this.VariableNames[i].Split('.');
                            if (splitName.Count() == 2)
                            {
                                // get specified grazeFoodStoreType
                                grazeStore = Resources.GetResourceItem(this, typeof(GrazeFoodStore), splitName[0], OnMissingResourceActionTypes.Ignore, OnMissingResourceActionTypes.ReportErrorAndStop) as GrazeFoodStoreType;

                                // make each pool entry
                                for (int j = 0; j <= 12; j++)
                                {
//                                    variableNames.Add(splitName[0] + "-" + j.ToString() + "-" + splitName[1]);
                                    variableNames.Add("[Resources].GrazeFoodStore."+splitName[0] + ".Pool(" + j.ToString() + ")." + splitName[1] + " as " + splitName[0] + "" + j.ToString() + "" + splitName[1]);
                                }
                                if (splitName[1] == "Amount")
                                {
                                    // add amounts
                                    variableNames.Add("[Resources].GrazeFoodStore." + splitName[0] + ".Amount as TotalAmount");
                                    variableNames.Add("[Resources].GrazeFoodStore." + splitName[0] + ".KilogramsPerHa as TotalkgPerHa");
                                }
                            }
                            else
                            {
                                throw new ApsimXException(this, "Invalid report property. Expecting full property link or GrazeFoodStoreTypeName.Property");
                            }
                        }
                    }
                }
                // check if clock.today was included.
                if(!variableNames.Contains("[Clock].Today"))
                {
                    variableNames.Insert(0, "[Clock].Today");
                }
            }
            // Tidy up variable/event names.
            VariableNames = variableNames.ToArray();
            VariableNames = TidyUpVariableNames();
            EventNames = TidyUpEventNames();
            this.FindVariableMembers();

            // Subscribe to events.
            if (EventNames == null || EventNames.Count() == 0)
            {
                events.Subscribe("[Clock].CLEMHerdSummary", DoOutputEvent);
            }
            else
            {
                foreach (string eventName in EventNames)
                {
                    if (eventName != string.Empty)
                    {
                        events.Subscribe(eventName.Trim(), DoOutputEvent);
                    }
                }
            }
        }

        [EventSubscribe("Completed")]
        private void OnCompleted(object sender, EventArgs e)
        {
            if (dataToWriteToDb != null)
            {
                storage.Writer.WriteTable(dataToWriteToDb);
            }
            dataToWriteToDb = null;
        }

        /// <summary>A method that can be called by other models to perform a line of output.</summary>
        public new void DoOutput()
        {
            if (dataToWriteToDb == null)
            {
                string folderName = null;
                var folderDescriptor = simulation.Descriptors.Find(d => d.Name == "FolderName");
                if (folderDescriptor != null)
                    folderName = folderDescriptor.Value;
                dataToWriteToDb = new ReportData()
                {
                    FolderName = folderName,
                    SimulationName = simulation.Name,
                    TableName = Name,
                    ColumnNames = columns.Select(c => c.Name).ToList(),
                    ColumnUnits = columns.Select(c => c.Units).ToList()
                };
            }

            // Get number of groups.
            var numGroups = Math.Max(1, columns.Max(c => c.NumberOfGroups));

            for (int groupIndex = 0; groupIndex < numGroups; groupIndex++)
            {
                // Create a row ready for writing.
                List<object> valuesToWrite = new List<object>();
                List<string> invalidVariables = new List<string>();
                for (int i = 0; i < columns.Count; i++)
                {
                    try
                    {
                        valuesToWrite.Add(columns[i].GetValue(groupIndex));
                    }
                    catch// (Exception err)
                    {
                        // Should we include exception message?
                        invalidVariables.Add(columns[i].Name);
                    }
                }
                if (invalidVariables != null && invalidVariables.Count > 0)
                    throw new Exception($"Error in report {Name}: Invalid report variables found:\n{string.Join("\n", invalidVariables)}");

                // Add row to our table that will be written to the db file
                dataToWriteToDb.Rows.Add(valuesToWrite);
            }

            // Write the table if we reach our threshold number of rows.
            if (dataToWriteToDb.Rows.Count >= 100)
            {
                storage.Writer.WriteTable(dataToWriteToDb);
                dataToWriteToDb = null;
            }

            DayAfterLastOutput = clock.Today.AddDays(1);
        }

        /// <summary>Create a text report from tables in this data store.</summary>
        /// <param name="storage">The data store.</param>
        /// <param name="fileName">Name of the file.</param>
        public static new void WriteAllTables(IDataStore storage, string fileName)
        {
            // Write out each table for this simulation.
            foreach (string tableName in storage.Reader.TableNames)
            {
                DataTable data = storage.Reader.GetData(tableName);
                if (data != null && data.Rows.Count > 0)
                {
                    SortColumnsOfDataTable(data);
                    StreamWriter report = new StreamWriter(Path.ChangeExtension(fileName, "." + tableName + ".csv"));
                    DataTableUtilities.DataTableToText(data, 0, ",", true, report);
                    report.Close();
                }
            }
        }

        /// <summary>Sort the columns alphabetically</summary>
        /// <param name="table">The table to sort</param>
        private static void SortColumnsOfDataTable(DataTable table)
        {
            var columnArray = new DataColumn[table.Columns.Count];
            table.Columns.CopyTo(columnArray, 0);
            var ordinal = -1;
            foreach (var orderedColumn in columnArray.OrderBy(c => c.ColumnName))
            {
                orderedColumn.SetOrdinal(++ordinal);
            }

            ordinal = -1;
            int i = table.Columns.IndexOf("SimulationName");
            if (i != -1)
            {
                table.Columns[i].SetOrdinal(++ordinal);
            }

            i = table.Columns.IndexOf("SimulationID");
            if (i != -1)
            {
                table.Columns[i].SetOrdinal(++ordinal);
            }
        }


        /// <summary>Called when one of our 'EventNames' events are invoked</summary>
        public new void DoOutputEvent(object sender, EventArgs e)
        {
            DoOutput();
        }

        /// <summary>
        /// Fill the Members list with VariableMember objects for each variable.
        /// </summary>
        private new void FindVariableMembers()
        {
            this.columns = new List<IReportColumn>();

            AddExperimentFactorLevels();

            // If a group by variable was specified then all columns need to be aggregated
            // columns. Find the first aggregated column so that we can, later, use its from and to
            // variables to create an agregated column that doesn't have them.
            string from = null;
            string to = null;
            if (!string.IsNullOrEmpty(GroupByVariableName))
                FindFromTo(out from, out to);

            foreach (string fullVariableName in this.VariableNames)
            {
                try
                {
                    if (!string.IsNullOrEmpty(fullVariableName))
                        columns.Add(new ReportColumn(fullVariableName, clock, locator, events, GroupByVariableName, from, to));
                }
                catch (Exception err)
                {
                    throw new Exception($"Error while creating report column '{fullVariableName}'", err);
                }
            }
        }

        /// <summary>Add the experiment factor levels as columns.</summary>
        private void AddExperimentFactorLevels()
        {
            if (simulation.Descriptors != null)
            {
                foreach (var descriptor in simulation.Descriptors)
                {
                    if (descriptor.Name != "Zone" && descriptor.Name != "SimulationName")
                    {
                        this.columns.Add(new ReportColumnConstantValue(descriptor.Name, descriptor.Value));
                    }
                }
            }
        }

    }
}
