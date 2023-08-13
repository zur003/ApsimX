﻿using Models.Core;
using Models.Interfaces;
using Models.PMF.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using Newtonsoft.Json;
using System.Linq;


namespace Models.Management
{
    /// <summary>
    /// Steps through each OrganBiomassRemoval child when Do() method is called and removes the specified fractions of biomass from each.  
    /// Organ names must match the name of an organ in the specified crop.  
    /// Biomass will only be removed from organs that are specified with an OrganBiomassRemoval child on this class.  
    /// Add Child of name "StageSet" to specify a phenology rewind when ever the Do() method is called
    /// </summary>
    [ValidParent(ParentType = typeof(Zone))]
    [ValidParent(ParentType = typeof(Folder))]
    [Serializable]
    [ViewName("UserInterface.Views.DualGridView")]
    [PresenterName("UserInterface.Presenters.TablePresenter")]
    public class BiomassRemovalFractions : Model, IModelAsTable
    {
        /// <summary>
        /// List of possible biomass removal types
        /// </summary>
        public enum BiomassRemovalType
        {
            /// <summary>Biomass is cut</summary>
            Cutting,
            /// <summary>Biomass is grazed</summary>
            Grazing,
            /// <summary>Biomass is harvested</summary>
            Harvesting,
            /// <summary>Biomass is pruned</summary>
            Pruning
        }

        /// <summary>Stores a row of Biomass Removal Fractions</summary>
        [Serializable]
        public class BiomassRemovalOfPlantOrganType {

            /// <summary>Name of the Crop this removal applies to</summary>
            public string PlantName { get; set; }

            /// <summary>Name of the Organ in the given crop that this removal applies to</summary>
            public string OrganName { get; set; }

            /// <summary>The type of removal this is</summary>
            [JsonIgnore]
            public BiomassRemovalType Type { get { return Enum.Parse<BiomassRemovalType>(TypeString); } }

            /// <summary></summary>
            public string TypeString { get; set; }

            /// <summary>Fraction of live biomass to remove from organ (0-1) </summary>
            [Units("g/m2")]
            [JsonIgnore]
            public double LiveToRemove { get { return Convert.ToDouble(LiveToRemoveString); } }

            /// <summary>Fraction of dea biomass to remove from organ (0-1) </summary>
            [Units("g/m2")]
            [JsonIgnore]
            public double DeadToRemove { get { return Convert.ToDouble(DeadToRemoveString); } }

            /// <summary>Fraction of live biomass to remove from organ and send to residues (0-1) </summary>
            [Units("g/m2")]
            [JsonIgnore]
            public double LiveToResidue { get { return Convert.ToDouble(LiveToResidueString); } }

            /// <summary>Fraction of live biomass to remove from organ and send to residue pool (0-1) </summary>
            [Units("g/m2")]
            [JsonIgnore]
            public double DeadToResidue { get { return Convert.ToDouble(DeadToResidueString); } }

            /// <summary></summary>
            public string LiveToRemoveString { get; set; }

            /// <summary></summary>
            public string DeadToRemoveString { get; set; }

            /// <summary></summary>
            public string LiveToResidueString { get; set; }

            /// <summary></summary>
            public string DeadToResidueString { get; set; }

            /// <summary>Default Constructor</summary>
            public BiomassRemovalOfPlantOrganType()
            {
                this.PlantName = null;
                this.OrganName = null;
                this.TypeString = null;
                this.LiveToRemoveString = null;
                this.DeadToRemoveString = null;
                this.LiveToResidueString = null;
                this.DeadToResidueString = null;
            }

            /// <summary></summary>
            public BiomassRemovalOfPlantOrganType(string plantName, string organName, string type, string liveToRemoveString, string deadToRemoveString, string liveToResidueString, string deadToResidueString)
            {
                this.PlantName = plantName;
                this.OrganName = organName;
                this.TypeString = type;
                this.LiveToRemoveString = liveToRemoveString;
                this.DeadToRemoveString = deadToRemoveString;
                this.LiveToResidueString = liveToResidueString;
                this.DeadToResidueString = deadToResidueString;
            }
        }

        /// <summary>Cutting Event</summary>
        public List<BiomassRemovalOfPlantOrganType> BiomassRemovals { get; set; }

        
        /// <summary>Cutting Event</summary>
        public event EventHandler<EventArgs> Cutting;

        /// <summary>Grazing Event</summary>
        public event EventHandler<EventArgs> Grazing;

        /// <summary>Pruning Event</summary>
        public event EventHandler<EventArgs> Pruning;

        /// <summary>Harvesting Event</summary>
        public event EventHandler<EventArgs> Harvesting;
        

        /// <summary>Constructor</summary>
        public BiomassRemovalFractions() {
            BiomassRemovals = new List<BiomassRemovalOfPlantOrganType>();
        }

        /// <summary>Gets or sets the description displayed in the GUI. These can be instructions on how to use the class.</summary>
        [JsonIgnore]
        public string Description
        {
            get { 
                return "List of all Plants and their Organs which share the same parent as this Biomass Removal Fractions\n" +
                       "Each option has a line for the type of Biomass Removal, positive numbers will remove Biomass.\n" +
                       "Call the Do() method on this class in a manager class to use the values stored to add/remove Biomass from the connected Organ"; 
            }
        }

        /// <summary>Gets or sets the table of values.</summary>
        [JsonIgnore]
        public List<DataTable> Tables
        {
            get
            {
                return new List<DataTable>() { GetGrid() };
            }
            set
            {
                SetGrid(value[0]);
            }
        }

        /// <summary>Return a table of all parameters.</summary>
        private DataTable GetGrid()
        {
            //Add table headers
            var data = new DataTable();
            data.Columns.Add("Plant");
            data.Columns.Add("Organ");
            data.Columns.Add("Type");
            data.Columns.Add("Live To Remove");
            data.Columns.Add("Dead To Remove");
            data.Columns.Add("Live To Residue");
            data.Columns.Add("Dead To Residue");
            data.Columns.Add(" "); // add an empty table so that the last column doesn't stretch across the screen

            //Create the list of options based on the crops in this sim
            List<IPlant> plants = this.FindAllSiblings<IPlant>().ToList();
            foreach (IPlant plant in plants)
            {
                List<IOrgan> organs = plant.FindAllDescendants<IOrgan>().ToList();
                foreach (IOrgan organ in organs)
                {
                    foreach (BiomassRemovalType type in Enum.GetValues(typeof(BiomassRemovalType)))
                    {
                        DataRow row = data.NewRow();
                        row["Plant"] = plant.Name;
                        row["Organ"] = organ.Name;
                        row["Type"] = type;
                        data.Rows.Add(row);
                    }
                }
            }

            //add in stored values
            foreach (BiomassRemovalOfPlantOrganType removal in BiomassRemovals)
            {
                //find which line of the data table matches the plant, organ and type fields
                foreach (DataRow row in data.Rows)
                {
                    if (row["Plant"].ToString().Equals(removal.PlantName) && row["Organ"].ToString().Equals(removal.OrganName) && row["Type"].ToString().Equals(removal.TypeString))
                    {
                        //matching row, fill in fractions
                        if (removal.LiveToRemoveString != null)
                            row["Live To Remove"] = removal.LiveToRemoveString;
                        if (removal.DeadToRemoveString != null)
                            row["Dead To Remove"] = removal.DeadToRemoveString;
                        if (removal.LiveToResidueString != null)
                            row["Live To Residue"] = removal.LiveToResidueString;
                        if (removal.DeadToResidueString != null)
                            row["Dead To Residue"] = removal.DeadToResidueString;
                    }
                }
            }

             return data;
        }

        /// <summary>
        /// Set the parameters property from a data table.
        /// </summary>
        /// <param name="data">The data table.</param>
        private void SetGrid(DataTable data)
        {
            BiomassRemovals.Clear();
            foreach (DataRow row in data.Rows)
            {
                string plantName = row["Plant"].ToString();
                string organName = row["Organ"].ToString();
                string type = row["Type"].ToString();
                string liveToRemove = null;
                if (!String.IsNullOrEmpty(row["Live To Remove"].ToString()))
                {
                    try
                    {
                        string input = Convert.ToDouble(row["Live To Remove"]).ToString();
                        liveToRemove = input;
                    } 
                    catch
                    {
                        liveToRemove = row["Live To Remove"].ToString();
                    }
                }

                string deadToRemove = null;
                if (!String.IsNullOrEmpty(row["Dead To Remove"].ToString()))
                {
                    try
                    {
                        string input = Convert.ToDouble(row["Dead To Remove"]).ToString();
                        deadToRemove = input;
                    }
                    catch
                    {
                        deadToRemove = row["Dead To Remove"].ToString();
                    }
                }

                string liveToResidue = null;
                if (!String.IsNullOrEmpty(row["Live To Residue"].ToString()))
                {
                    try
                    {
                        string input = Convert.ToDouble(row["Live To Residue"]).ToString();
                        liveToResidue = input;
                    }
                    catch
                    {
                        liveToResidue = row["Live To Residue"].ToString();
                    }
                }

                string deadToResidue = null;
                if (!String.IsNullOrEmpty(row["Dead To Residue"].ToString()))
                {
                    try
                    {
                        string input = Convert.ToDouble(row["Dead To Residue"]).ToString();
                        deadToResidue = input;
                    }
                    catch
                    {
                        deadToResidue = row["Dead To Residue"].ToString();
                    }
                }

                BiomassRemovals.Add(new BiomassRemovalOfPlantOrganType(plantName, organName, type, liveToRemove, deadToRemove, liveToResidue, deadToResidue));
            }
            return;
        }

        /// <summary>Method that applies specified removal fractions and rewind</summary>
        public void Do(BiomassRemovalType removaltype)
        {

            foreach (BiomassRemovalOfPlantOrganType removal in BiomassRemovals)
            {
                if (removal.Type == removaltype)
                {
                    double liveToRemoved = 0;
                    try
                    {
                        if (!String.IsNullOrEmpty(removal.LiveToRemoveString))
                            liveToRemoved = removal.LiveToRemove;
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Error parsing Biomass Removal Variable 'LiveToRemove'. Value {removal.LiveToRemoveString} could not be parsed to number.", e);
                    }

                    double deadToRemoved = 0;
                    try
                    {
                        if (!String.IsNullOrEmpty(removal.DeadToRemoveString))
                            deadToRemoved = removal.DeadToRemove;
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Error parsing Biomass Removal variable 'DeadToRemove'. Value {removal.DeadToRemoveString} could not be parsed to number.", e);
                    }

                    double liveToResidues = 0;
                    try
                    {
                        if (!String.IsNullOrEmpty(removal.LiveToResidueString))
                            liveToResidues = removal.LiveToResidue;
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Error parsing Biomass Removal variable 'LiveToResidue'. Value {removal.LiveToResidueString} could not be parsed to number.", e);
                    }

                    double deadToResidues = 0;
                    try
                    {
                        if (!String.IsNullOrEmpty(removal.DeadToResidueString))
                            deadToResidues = removal.DeadToResidue;
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Error parsing Biomass Removal variable 'DeadToResidue'. Value {removal.DeadToResidueString} could not be parsed to number.", e);
                    }

                    IPlant plant = this.FindSibling<IPlant>(removal.PlantName);
                    IOrgan organ = plant.FindDescendant<IOrgan>(removal.OrganName);

                    (organ as IHasDamageableBiomass).RemoveBiomass(liveToRemove: liveToRemoved,
                                                                    deadToRemove: deadToRemoved,
                                                                    liveToResidue: liveToResidues,
                                                                    deadToResidue: deadToResidues);
                }
            }

            if (removaltype.ToString() == BiomassRemovalType.Cutting.ToString())
                Cutting?.Invoke(this, new EventArgs());
            if (removaltype.ToString() == BiomassRemovalType.Grazing.ToString())
                Grazing?.Invoke(this, new EventArgs());
            if (removaltype.ToString() == BiomassRemovalType.Pruning.ToString())
                Pruning?.Invoke(this, new EventArgs());
            if (removaltype.ToString() == BiomassRemovalType.Harvesting.ToString())
                Harvesting?.Invoke(this, new EventArgs());
        }
    }
}