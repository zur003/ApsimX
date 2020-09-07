﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Models.Core;
using Models.Interfaces;

namespace Models.Functions.SupplyFunctions
{
    /// <summary>
    /// This model calculates the CO<sub>2</sub> impact on stomatal conductance using the approach of Elli et al (2020).
    /// </summary>
    [Serializable]
    [Description("This model calculates CO2 Impact on stomatal conductance RUE using the approach of <br>Elli et al (2020) <br>Global sensitivity-based modelling approach to identify suitable Eucalyptus traits for adaptation to climate variability and change. <br> in silico Plants Vol. 2, No. 1, pp. 1–17")]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(IFunction))]
    public class StomatalConductanceCO2Modifier : Model, IFunction
    {
        /// <summary>The met data</summary>
        [Link]
        protected IWeather MetData = null;

        /// <summary>Photosynthesis CO2 Modifier</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        IFunction PhotosynthesisCO2Modifier = null;

        /// <summary>Gets the value.</summary>
        /// <value>The value.</value>
        public double Value(int arrayIndex = -1)
        {
                if (MetData.CO2 < 350)
                    throw new Exception("CO2 concentration too low for Stomatal Conductance CO2 Function");
                else if (MetData.CO2 == 350)
                    return 1.0;
                else
                {
                    double temp = (MetData.MaxT + MetData.MinT) / 2.0; // Average temperature
                    double CP = (163.0 - temp) / (5.0 - 0.1 * temp);  //co2 compensation point (ppm)

                    double first = (MetData.CO2 - CP);
                    double second = (350.0 - CP);
                    return PhotosynthesisCO2Modifier.Value() / (first / second);
                }
        }
    }
}