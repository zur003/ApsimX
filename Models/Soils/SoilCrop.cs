﻿namespace Models.Soils
{
    using APSIM.Shared.Utilities;
    using Models.Core;
    using System;

    /// <summary>A soil crop parameterization class.</summary>
    [Serializable]
    [ViewName("UserInterface.Views.ProfileView")]
    [PresenterName("UserInterface.Presenters.ProfilePresenter")]
    [ValidParent(ParentType = typeof(Physical))]
    public class SoilCrop : Model
    {
        /// <summary>Crop lower limit (mm/mm)</summary>
        [Summary]
        [Description("LL")]
        [Units("mm/mm")]
        public double[] LL { get; set; }

        /// <summary>Crop lower limit (mm)</summary>
        [Units("mm")]
        public double[] LLmm
        {
            get
            {
                var soilPhysical = FindAncestor<IPhysical>();
                if (soilPhysical == null)
                    return null;
                return MathUtilities.Multiply(LL, soilPhysical.Thickness);
            }
        }

    /// <summary>The KL value.</summary>
    [Summary]
        [Description("KL")]
        [Display(Format = "N2")]
        [Units("/day")]
        public double[] KL { get; set; }

        /// <summary>The exploration factor</summary>
        [Summary]
        [Description("XF")]
        [Display(Format = "N1")]
        [Units("0-1")]
        public double[] XF { get; set; }

        /// <summary>The metadata for crop lower limit</summary>
        public string[] LLMetadata { get; set; }

        /// <summary>The metadata for KL</summary>
        public string[] KLMetadata { get; set; }

        /// <summary>The meta data for the exploration factor</summary>
        public string[] XFMetadata { get; set; }

        /// <summary>Return the plant available water CAPACITY at standard thickness.</summary>
        [Description("PAWC")]
        [Display(Format = "N2", ShowTotal = true)]
        [Units("mm/mm")]
        public double[] PAWC
        {
            get
            {
                var soilPhysical = FindAncestor<IPhysical>();
                if (soilPhysical == null)
                    return null;
                return SoilUtilities.CalcPAWC(soilPhysical.Thickness, LL, soilPhysical.DUL, XF);
            }
        }
    }
}