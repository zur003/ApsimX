using System;
using Models.Core;
using Models.Soils;
using APSIM.Shared.Utilities;
using Models.Interfaces;

namespace Models.Functions
{
    /// <summary>
    /// # [Name]
    /// A simple scale to convert soil water content into a value between 0 and 2 where 0 = LL15, 1 = DUL and 2 = SAT
    /// </summary>
    [Serializable]
    public class SoilWaterScale : Model, IFunction
    {
        [Link]
        Physical physical = null;

        [Link]
        ISoilWater soilwater = null;

        /// <summary>Gets the value of the function.</summary>
        public double Value(int arrayIndex = -1)
        {
            if (arrayIndex == -1)
                return 1;

            double sws;

            if (soilwater.SW[arrayIndex] >= physical.SAT[arrayIndex])                 // saturated - 2
                sws = 2;
            else if (soilwater.SW[arrayIndex] >= physical.DUL[arrayIndex])            // draining - 1 to 2
                sws = 1.0 + MathUtilities.Divide(soilwater.SW[arrayIndex] - physical.DUL[arrayIndex], physical.SAT[arrayIndex] - physical.DUL[arrayIndex], 0.0);
            else if (soilwater.SW[arrayIndex] > physical.LL15[arrayIndex])            // unsaturated - 0 to 1
                sws = MathUtilities.Divide(soilwater.SW[arrayIndex] - physical.LL15[arrayIndex], physical.DUL[arrayIndex] - physical.LL15[arrayIndex], 0.0);
            else                                                                      // dry  - 0
                sws = 0.0;

            return sws;
        }

    }
}