﻿using APSIM.Shared.Utilities;
using Models.Storage;
using System.Data;
using System.Text.RegularExpressions;

namespace Gtk.Sheet
{
    /// <summary>
    /// Provides paged access to a table in the DataStore.
    /// </summary>
    public class PagedDataProvider : IDataProvider
    {
        /// <summary>The data store.</summary>
        private readonly IStorageReader dataStore;

        /// <summary>The data table.</summary>
        private readonly List<DataPage> dataPages = new List<DataPage>();

        /// <summary>The number of rows to load at a time from the datastore table.</summary>
        private readonly int pageSize;

        /// <summary>The name of the checkpoint.</summary>
        private readonly string checkpointName;
        
        /// <summary>The name of the table.</summary>
        private readonly string tableName;

        /// <summary>The names of the simulations in scope.</summary>
        private readonly IEnumerable<string> simulationNames;

        /// <summary>The row filter.</summary>
        private readonly string rowFilter;

        /// <summary>The names of the columns to read from table.</summary>
        private IEnumerable<string> columnNames;

        /// <summary>The optional units for each column in the data table. Can be null.</summary>
        private IList<string> units;

        /// <summary>The names of the columns to read from table.</summary>
        private IEnumerable<string> columnNameFilters;

        /// <summary>
        /// If the store is a DataStoreReader, points to its Connection.
        /// This allows us to use SQL appropriate for different connection types.
        /// </summary>
        private IDatabaseConnection connection = null;

        /// <summary>Column to order by</summary>
        private string orderBy;

        /// <summary>Constructor.</summary>
        /// <param name="store">The data store.</param>
        /// <param name="dataTableName">Name of table to read.</param>
        /// <param name="checkpointNameInScope">Name of checkpoint to load.</param>
        /// <param name="simulationNamesInScope">The names of simulations in scope.</param>
        /// <param name="columnNameFilter">Column name filter (csv). Can be null.</param>
        /// <param name="dataFilter">The data filter to apply.</param>
        /// <param name="sortBy">Column to sort by</param>
        /// <param name="dataPageSize">The number of rows to load at a time from the datastore table.</param>
        public PagedDataProvider(IStorageReader store, 
                                 string checkpointNameInScope,
                                 string dataTableName,
                                 IEnumerable<string> simulationNamesInScope,
                                 string columnNameFilter,
                                 string dataFilter,
                                 string sortBy = "",
                                 int dataPageSize = 50)
        {
            dataStore = store;
            if (dataStore is DataStoreReader)
                connection = (dataStore as DataStoreReader).Connection;
            checkpointName = checkpointNameInScope;
            tableName = dataTableName;
            simulationNames = simulationNamesInScope;
            rowFilter = dataFilter;
            pageSize = dataPageSize;
            orderBy = sortBy;
            CreateTemporaryKeyset();
            GetColumnNames(columnNameFilter);
            GetData(0);
            GetUnits();
            GetRowCount();
        }

        /// <summary>Cleanup the instance.</summary>
        public void Cleanup()
        {
            if (connection is SQLite)
                dataStore.ExecuteSql("DROP TABLE IF EXISTS keyset");
        }

        /// <summary>Invoked when the paging is about to start.</summary>
        public event EventHandler PagingStart;

        /// <summary>Invoked when the paging has ended.</summary>
        public event EventHandler PagingEnd;

        /// <summary>An event invoked when a cell changes.</summary>
        public event IDataProvider.CellChangedDelegate CellChanged;        

        /// <summary>Number of columns that are always to be visible.</summary>
        public int NumPriorityColumns { get; set; }

        /// <summary>Gets the number of columns of data.</summary>
        public int ColumnCount => dataPages[0].ColumnCount;

        /// <summary>Gets the number of rows of data.</summary>
        public int RowCount { get; private set; }

        /// <summary>Get the name of a column.</summary>
        /// <param name="columnIndex">Column index.</param>
        public string GetColumnName(int columnIndex)
        {
            if (columnIndex < ColumnCount)
                return dataPages[0].GetColumnName(columnIndex);
            throw new Exception($"Invalid column index: {columnIndex}");
        }

        /// <summary>Get the units of a column.</summary>
        /// <param name="columnIndex">Column index.</param>
        public string GetColumnUnits(int columnIndex)
        {
            if (units == null)
                return null;
            if (columnIndex < units.Count)
                return units[columnIndex];
            throw new Exception($"Invalid column index: {columnIndex}");
        }

        /// <summary>Get the allowable units of a column.</summary>
        /// <param name="columnIndex">Column index.</param>
        public IReadOnlyList<string> GetColumnValidUnits(int columnIndex)
        {
            if (units == null)
                return null;
            if (columnIndex < units.Count)
                return new string[] { units[columnIndex] };
            throw new Exception($"Invalid column index: {columnIndex}");            
        }        

        /// <summary>Get the contents of a cell.</summary>
        /// <param name="columnIndex">Column index of cell.</param>
        /// <param name="rowIndex">Row index of cell.</param>
        public string GetCellContents(int columnIndex, int rowIndex)
        {
            // Load more data if necessary.
            var dataPage = dataPages.Find(d => d.Contains(rowIndex));
            if (dataPage == null)
                dataPage = GetData(rowIndex);

            object value = dataPage.GetCellContents(columnIndex, rowIndex);

            if (value is double)
                return ((double)value).ToString("F3");  // 3 decimal places.
            else if (value is DateTime)
                return ((DateTime)value).ToString("yyyy-MM-dd");
            return value.ToString();
        }

        /// <summary>Set the contents of a cell.</summary>
        /// <param name="colIndex">Column index of cell.</param>
        /// <param name="rowIndex">Row index of cell.</param>
        /// <param name="value">The value.</param>
        public void SetCellContent(int colIndex, int rowIndex, string value)
        {
            SetCellContents(new int[] { colIndex }, new int[] { rowIndex }, new string[] { value });
        }

        /// <summary>Set the contents of multiple cells.</summary>
        /// <param name="colIndices">Column index of cells.</param>
        /// <param name="rowIndices">Row index of cells.</param>
        /// <param name="values">The value of the cells.</param>
        public void SetCellContents(int[] columnIndices, int[] rowIndices, string[] values)
        {
            CellChanged?.Invoke(this, columnIndices, rowIndices, values);
        }

        /// <summary>Delete the specified rows.</summary>
        /// <param name="rowIndices">Row indexes of cell.</param>
        public void DeleteRows(int[] rowIndices)
        {
            throw new NotImplementedException("Cannot deleta a row of a paged data table");
        }

        /// <summary>Get data to show in grid.</summary>
        /// <param name="startRowIndex">The row index to start getting data from.</param>
        /// <param name="orderBy">Column to order by</param>
        private DataPage GetData(int startRowIndex)
        {
            PagingStart?.Invoke(this, new EventArgs());
            DataTable newData = null;

            List<string> sort = new List<string>();
            if (orderBy.Length > 0) 
            {
                sort.Add(orderBy);
                sort.Add("SimulationID");
                sort.Add("Clock.Today");
            }

            newData = dataStore.GetData(tableName,
                                        checkpointName,
                                        simulationNames,
                                        columnNames,
                                        GetRollingCursorRowFilter(startRowIndex, pageSize),
                                        0, 
                                        0,
                                        sort);
 
            // Remove unwanted columns from data table.
            foreach (string columnName in DataTableUtilities.GetColumnNames(newData))
                if (!columnNames.Contains(columnName))
                    newData.Columns.Remove(columnName);

            var newPage = new DataPage(newData, startRowIndex);

            dataPages.Add(newPage);

            PagingEnd?.Invoke(this, new EventArgs());
            return newPage;
        }

        /// <summary>Create a temporary keyset of rowids.</summary>
        /// <remarks>This concept of a rolling cursor comes from: https://sqlite.org/forum/forumpost/2cfa137263</remarks>
        private void CreateTemporaryKeyset()
        {
            string filter = GetFilter();
            string sql = String.Empty;
            string sort = String.Empty;

            sort = "ORDER BY \"SimulationID\", \"Clock.Today\"";
            if (orderBy.Length > 0)
                sort = $"ORDER BY \"{orderBy}\", \"SimulationID\"";
            Cleanup();
            sql = "CREATE TEMPORARY TABLE keyset AS " +
                     $"SELECT rowid FROM \"{tableName}\" ";
            if (!string.IsNullOrEmpty(filter))
                sql += $"WHERE {filter} ";

            sql += sort;
            dataStore.GetDataUsingSql(sql);
        }

        /// <summary>Gets a filter that includes rowid to implement data pagination (rolling cursor).</summary>
        /// <param name="from"></param>
        /// <param name="count"></param>
        /// <param name="orderBy"></param>
        /// <returns></returns>
        private string GetRollingCursorRowFilter(int from, int count)
        {
            string filter = GetFilter();

            DataTable data = null;
            data = dataStore.GetDataUsingSql($"SELECT \"rowid\" FROM \"keyset\" LIMIT {count} OFFSET {from}");

            if (data is null)
                return "";

            var rowIds = DataTableUtilities.GetColumnAsLongInts(data, "rowid");
            var rowIdsCSV = StringUtilities.Build(rowIds, ",");

            string returnFilter = String.Empty;
            if (!String.IsNullOrEmpty(rowIdsCSV))
            {
                returnFilter = $"\"rowid\" in ({rowIdsCSV})";
                if (!string.IsNullOrEmpty(filter))
                    returnFilter += $" AND ({filter})";
            }
            else 
                returnFilter = filter;
            return returnFilter;
        }

        /// <summary>Get units for columns.</summary>
        private void GetUnits()
        {
            // Get units for all columns
            units = new List<string>();
            foreach (var columnName in columnNames)
                units.Add(dataStore.Units(tableName, columnName));

            // If all units are empty then null the units list.
            if (!units.Where(unit => !string.IsNullOrEmpty(unit)).Any())
                units = null;
        }

        /// <summary>Calculate the row count.</summary>
        private void GetRowCount()
        {
            string filter = GetFilter();
            var sql = $"SELECT COUNT(*) FROM \"{tableName}\"";
            if (!string.IsNullOrEmpty(filter))
                sql += $" WHERE {filter}";
            
            // Null conditional used to handle the edge case where data does not contain CheckpointID
            var table = dataStore.GetDataUsingSql(sql) ?? dataStore.GetDataUsingSql($"SELECT COUNT(*) FROM {tableName}");
            RowCount = Convert.ToInt32(table.Rows[0][0]); // add a row for headings.
        }

        /// <summary>Create a sql filter</summary>
        private string GetFilter()
        {
            var filter = rowFilter;

            if (filter != null)
            {
                // if the filter has [SimulationName] = 'aaaa' then replace it with
                // SimulationID = b
                string pattern = @"\[?SimulationName\]?\s*=\s*'(\w+)'";
                Match match;
                while ((match = Regex.Match(filter, pattern)).Success)
                {
                    string simulationName = match.Groups[1].ToString();
                    var simulationID = dataStore.ToSimulationIDs(new string[] { simulationName })?.First();
                    if (simulationID > 0)
                    {
                        string replacement = $"SimulationID={simulationID}";
                        filter = filter.Remove(match.Index, match.Length);
                        filter = filter.Insert(match.Index, replacement);
                    }
                }
            }

            string checkpointFilter = $"\"CheckpointID\" = {dataStore.GetCheckpointID(checkpointName)}";
            if (string.IsNullOrEmpty(filter))
                filter = checkpointFilter;
            else
                filter += $" AND {checkpointFilter}";

            if (simulationNames != null)
            {
                var simulationFilter = $"\"SimulationID\" in ({dataStore.ToSimulationIDs(simulationNames).Join(",")})";
                if (string.IsNullOrEmpty(filter))
                    filter = simulationFilter;
                else
                    filter += " AND " + simulationFilter;
            }

            return filter;
        }

        /// <summary>Get the names of all columns to read from data store.</summary>
        /// <param name="columnNameFilter">The column name filter to use.</param>
        private void GetColumnNames(string columnNameFilter)
        {
            // Get a list of column names to read from the datastore.
            var rawColumnNames = dataStore.ColumnNames(tableName).ToList();

            // Strip out unwanted columns.
            var unwantedColumns = new string[] { "CheckpointName", "CheckpointID", "SimulationID" };
            rawColumnNames.RemoveAll(name => unwantedColumns.Contains(name));

            // Determine columns that always appear at start of grid: Date, SimulationName, Zone
            var priorityColumns = rawColumnNames.Where(name => name.Contains("Date") || name.Contains("Today"))
                                                .Concat(new string[] { "SimulationName" })
                                                .Concat(rawColumnNames.Where(name => name == "Zone"));

            NumPriorityColumns = priorityColumns.Count();

            if (!string.IsNullOrEmpty(columnNameFilter))
            {
                columnNameFilters = columnNameFilter.Split(',').Where(x => !string.IsNullOrEmpty(x));
                columnNames = priorityColumns.Concat(rawColumnNames.Where(c => ColumnMatchesFilter(c)));
            }
            else
                columnNames = priorityColumns.Concat(rawColumnNames.Except(priorityColumns));
        }

        /// <summary>Returns true if a column name matches the column filter.</summary>
        /// <param name="columnName">The column name.</param>
        private bool ColumnMatchesFilter(string columnName)
        {
            
            foreach (var columnNameFilter in columnNameFilters)
                if (columnName.Contains(columnNameFilter))
                    return true;
            return false;
        }

        /// <summary>Get the cell state.</summary>
        /// <param name="colIndex">Column index of cell.</param>
        /// <param name="rowIndex">Row index of cell.</param>
        public SheetCellState GetCellState(int colIndex, int rowIndex)
        {
            return SheetCellState.ReadOnly;
        }

        /// <summary>Set the cell state.</summary>
        /// <param name="colIndex">Column index of cell.</param>
        /// <param name="rowIndex">Row index of cell.</param>
        public void SetCellState(int colIndex, int rowIndex)
        {
            throw new NotImplementedException("Cannot set state of a PagedDataProvider");
        }

        /// <summary>
        ///  Encapsulates a page of DataTable rows.
        /// </summary>
        private class DataPage
        {
            /// <summary>The data table.</summary>
            private readonly DataTable data;
            
            /// <summary>The index of the start row.</summary>
            private readonly int start;


            /// <summary>Constructor.</summary>
            /// <param name="table">The data table.</param>
            /// <param name="startRowIndex">The index of the start row.</param>
            public DataPage(DataTable table, int startRowIndex)
            {
                data = table;
                start = startRowIndex;
            }

            /// <summary>Returns true if a row is within the data page instance.</summary>
            /// <param name="rowIndex">The row index.</param>
            public bool Contains(int rowIndex)
            {
                return rowIndex >= start && rowIndex < start + data.Rows.Count;
            }

            /// <summary>The number of columns.</summary>
            public int ColumnCount => data.Columns.Count;

            /// <summary>Gets a column name.</summary>
            /// <param name="columnIndex">The column index.</param>
            public string GetColumnName(int columnIndex)
            {
                return data.Columns[columnIndex].ColumnName;
            }

            /// <summary>Gets the contents of a cell.</summary>
            /// <param name="columnIndex">The column index.</param>
            /// <param name="rowIndex">The row index.</param>
            /// <returns></returns>
            public object GetCellContents(int columnIndex, int rowIndex)
            {
                return data.Rows[rowIndex - start][columnIndex];
            }
        }
    }
}
