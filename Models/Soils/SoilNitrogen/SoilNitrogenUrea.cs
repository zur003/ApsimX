﻿namespace Models.Soils
{
    using Models.Core;
    using Newtonsoft.Json;
    using System;

    /// <summary>This class encapsulates a SoilNitrogen model solute.</summary>
    [Serializable]
    [ViewName("ApsimNG.Resources.Glade.ProfileView.glade")]
    [PresenterName("UserInterface.Presenters.ProfilePresenter")]
    [ValidParent(ParentType = typeof(Soil))]
    public class SoilNitrogenUrea : Solute
    {
        [Link]
        SoilNitrogen soilNitrogen = null;

        /// <summary>Solute amount (kg/ha)</summary>
        [JsonIgnore]
        public override double[] kgha
        {
            get
            {
                return SoilNitrogen.CalculateUrea();
            }
            set
            {
                SetKgHa(SoluteSetterType.Plant, value);
            }
        }
                
        /// <summary>Setter for kgha.</summary>
        /// <param name="callingModelType">Type of calling model.</param>
        /// <param name="value">New values.</param>
        public override void SetKgHa(SoluteSetterType callingModelType, double[] value)
        {
            SoilNitrogen.SetUrea(callingModelType, value);
        }

        /// <summary>Setter for kgha delta.</summary>
        /// <param name="callingModelType">Type of calling model</param>
        /// <param name="delta">New delta values</param>
        public override void AddKgHaDelta(SoluteSetterType callingModelType, double[] delta)
        {
            SoilNitrogen.SetUreaDelta(callingModelType, delta);
        }

        /// <summary>The SoilNitrogen node.</summary>
        private SoilNitrogen SoilNitrogen
        {
            get
            {
                if (soilNitrogen == null)
                    soilNitrogen = FindInScope<SoilNitrogen>();
                return soilNitrogen;
            }
        }
    }
}
