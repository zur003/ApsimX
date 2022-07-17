﻿

namespace Models.Soils
{
    using Core;
    using Interfaces;
    using System;
    using APSIM.Shared.Utilities;
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using APSIM.Shared.Documentation;
    using System.Data;
    using System.Linq;

    /// <summary>
    /// This class used for this nutrient encapsulates the nitrogen within a mineral N pool.
    /// Child functions provide information on flows of N from it to other mineral N pools,
    /// or losses from the system.
    /// </summary>
    [Serializable]
    [ViewName("ApsimNG.Resources.Glade.ProfileView.glade")]
    [PresenterName("UserInterface.Presenters.ProfilePresenter")]
    [ValidParent(ParentType = typeof(Soil))]
    public class Solute : Model, ISolute, ITabularData
    {
        /// <summary>Access the soil physical properties.</summary>
        [Link] 
        private IPhysical physical = null;

        /// <summary>
        /// An enumeration for specifying soil water units
        /// </summary>
        public enum UnitsEnum
        {
            /// <summary>ppm</summary>
            [Description("ppm")]
            ppm,

            /// <summary>kgha</summary>
            [Description("kg/ha")]
            kgha
        }

        /// <summary>Default constructor.</summary>
        public Solute() { }

        /// <summary>Default constructor.</summary>
        public Solute(string soluteName, double[] value) 
        {
            kgha = value;
            Name = soluteName;
        }

        /// <summary>Depth strings. Wrapper around Thickness.</summary>
        [Summary]
        [Units("mm")]
        [JsonIgnore]
        public string[] Depth
        {
            get => SoilUtilities.ToDepthStrings(Thickness);
            set => Thickness = SoilUtilities.ToThickness(value);
        }

        /// <summary>Thickness</summary>
        public double[] Thickness { get; set; }

        /// <summary>Nitrate NO3.</summary>
        [Summary]
        [Display(Format = "N3")]
        public double[] InitialValues { get; set; }

        /// <summary>Units of the Initial values.</summary>
        public UnitsEnum InitialValuesUnits { get; set; }

        /// <summary>Concentration of solute in water table (ppm).</summary>
        [Description("For SWIM: Concentration of solute in water table (ppm).")]
        public double WaterTableConcentration { get; set; }

        /// <summary>Diffusion coefficient (D0).</summary>
        [Description("For SWIM: Diffusion coefficient (D0)")]
        public double D0 { get; set; }

        /// <summary>EXCO.</summary>
        public double[] Exco { get; set; }

        /// <summary>FIP.</summary>
        public double[] FIP { get; set; }

        /// <summary>Solute amount (kg/ha)</summary>
        [JsonIgnore]
        public virtual double[] kgha { get; set; }

        /// <summary>Solute amount (ppm)</summary>
        public double[] ppm { get { return SoilUtilities.kgha2ppm(Physical.Thickness, Physical.BD, kgha); } }

        /// <summary>Performs the initial checks and setup</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("StartOfSimulation")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            Reset();
        }

        /// <summary>
        /// Set solute to initialisation state
        /// </summary>
        public virtual void Reset()
        {
            if (InitialValues == null)
                kgha = new double[Thickness.Length];
            else if (InitialValuesUnits == UnitsEnum.kgha)
                kgha = ReflectionUtilities.Clone(InitialValues) as double[];
            else
                kgha = SoilUtilities.ppm2kgha(Thickness, SoluteBD, InitialValues);
        }

        /// <summary>Setter for kgha.</summary>
        /// <param name="callingModelType">Type of calling model.</param>
        /// <param name="value">New values.</param>
        public virtual void SetKgHa(SoluteSetterType callingModelType, double[] value)
        {
            for (int i = 0; i < value.Length; i++)
                kgha[i] = value[i];
        }

        /// <summary>Setter for kgha delta.</summary>
        /// <param name="callingModelType">Type of calling model</param>
        /// <param name="delta">New delta values</param>
        public virtual void AddKgHaDelta(SoluteSetterType callingModelType, double[] delta)
        {
            for (int i = 0; i < delta.Length; i++)
                kgha[i] += delta[i];
        }

        /// <summary>
        /// Document the model.
        /// </summary>
        public override IEnumerable<ITag> Document()
        {
            foreach (ITag tag in DocumentChildren<Memo>())
                yield return tag;

            foreach (ITag tag in GetModelDescription())
                yield return tag;
        }

        /// <summary>Tabular data. Called by GUI.</summary>
        public TabularData GetTabularData()
        {
            bool swimPresent = FindInScope<Swim3>() != null || Parent is Factorial.Factor;
            var columns = new List<TabularData.Column>()
            {
                new TabularData.Column("Depth", new VariableProperty(this, GetType().GetProperty("Depth"))),
                new TabularData.Column("Initial values", new VariableProperty(this, GetType().GetProperty("InitialValues")))
            };
            if (swimPresent)
            {
                columns.Add(new TabularData.Column("EXCO", new VariableProperty(this, GetType().GetProperty("Exco"))));
                columns.Add(new TabularData.Column("FIP", new VariableProperty(this, GetType().GetProperty("FIP"))));
            }
            return new TabularData(Name, columns);
        }

        /// <summary>Gets the model ready for running in a simulation.</summary>
        /// <param name="targetThickness">Target thickness.</param>
        public void Standardise(double[] targetThickness)
        {
            // Define default ppm value to use below bottom layer of this solute if necessary.
            double defaultValue;
            if (Name.Equals("NO3", StringComparison.InvariantCultureIgnoreCase))
                defaultValue = 1.0;
            else if (Name.Equals("NH4", StringComparison.InvariantCultureIgnoreCase))
                defaultValue = 0.2;
            else
                defaultValue = 0.0;

            SetThickness(targetThickness, defaultValue);

            if (FIP != null) FIP = MathUtilities.FillMissingValues(FIP, Thickness.Length, FIP.Last());
            if (Exco != null) Exco = MathUtilities.FillMissingValues(Exco, Thickness.Length, Exco.Last());
            InitialValues = MathUtilities.FillMissingValues(InitialValues, Thickness.Length, defaultValue);
            Reset();
        }

        /// <summary>Sets the sample thickness.</summary>
        /// <param name="thickness">The thickness to change the sample to.</param>
        /// <param name="defaultValue">Default value for missing values.</param>
        private void SetThickness(double[] thickness, double defaultValue)
        {
            if (!MathUtilities.AreEqual(thickness, Thickness))
            {
                if (Exco != null)
                    Exco = SoilUtilities.MapConcentration(Exco, Thickness, thickness, 0.2);
                if (FIP != null)
                    FIP = SoilUtilities.MapConcentration(FIP, Thickness, thickness, 0.2);

                double[] ppm = InitialValues;
                if (InitialValuesUnits == UnitsEnum.kgha)
                    ppm = SoilUtilities.kgha2ppm(Thickness, SoluteBD, InitialValues);
                InitialValues = SoilUtilities.MapConcentration(ppm, Thickness, thickness, defaultValue);
                InitialValuesUnits = Solute.UnitsEnum.ppm;
                Thickness = thickness;
            }
        }

        /// <summary>The soil physical node.</summary>
        protected IPhysical Physical
        {
            get
            {
                if (physical == null)
                    physical = FindInScope<IPhysical>();
                return physical;
            }
        }

        /// <summary>Return bulk density on the same layer structure as this solute.</summary>
        private double[] SoluteBD => SoilUtilities.MapConcentration(Physical.BD, Physical.Thickness, Thickness, Physical.BD.Last());
    }
}
