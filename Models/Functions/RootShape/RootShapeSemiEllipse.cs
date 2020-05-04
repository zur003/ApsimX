﻿using System;
using System.Collections.Generic;
using Models.Core;
using Models.Interfaces;
using APSIM.Shared.Utilities;
using Models.PMF;
using Models.PMF.Organs;

namespace Models.Functions.RootShape
{
    /// <summary>
    /// This model calculates the proportion of each soil layer occupided by roots.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Root))]
    public class RootShapeSemiEllipse : Model, IRootShape, ICustomDocumentation
    {
        /// <summary>The Root Angle</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        [Units("Degree")]
        private readonly IFunction RootAngle = null;

        /// <summary>The Root Angle for which soil LL values were estimated</summary>
        [Link(Type = LinkType.Child, ByName = true, IsOptional = true)]
        [Units("Degree")]
        private readonly IFunction RootAngleBase = null;

        /// <summary>Calculates the root area for a layer of soil</summary>
        public void CalcRootProportionInLayers(ZoneState zone)
        {
            zone.RootArea = 0;
            for (int layer = 0; layer < zone.soil.Thickness.Length; layer++)
            {
                double prop;
                double top = layer == 0 ? 0 : MathUtilities.Sum(zone.soil.Thickness, 0, layer - 1);
                double bottom = top + zone.soil.Thickness[layer];
                double rootArea, rootAreaBaseUnlimited, rootAreaUnlimited, llModifer;

                if (RootAngleBase != null && RootAngleBase.Value() != RootAngle.Value())
                {
                    // Root area for the base and current root angle when not limited by adjacent rows
                    rootAreaBaseUnlimited = CalcRootAreaSemiEllipse(zone, RootAngleBase.Value(), top, bottom, 10000);   // Right side
                    rootAreaBaseUnlimited += CalcRootAreaSemiEllipse(zone, RootAngleBase.Value(), top, bottom, 10000);   // Left Side
                    rootAreaUnlimited = CalcRootAreaSemiEllipse(zone, RootAngle.Value(), top, bottom, 10000);   // Right side
                    rootAreaUnlimited += CalcRootAreaSemiEllipse(zone, RootAngle.Value(), top, bottom, 10000);   // Left Side
                    llModifer = MathUtilities.Divide(rootAreaUnlimited, rootAreaBaseUnlimited, 1);
                } else
                {
                    llModifer = 1;
                }

                rootArea = CalcRootAreaSemiEllipse(zone, RootAngle.Value(), top, bottom, zone.RightDist);   // Right side
                rootArea += CalcRootAreaSemiEllipse(zone, RootAngle.Value(), top, bottom, zone.LeftDist);   // Left Side
                zone.RootArea += rootArea / 1e6;

                double soilArea = (zone.RightDist + zone.LeftDist) * (bottom - top);
                prop = Math.Max(0.0, MathUtilities.Divide(rootArea, soilArea, 0.0));

                zone.RootProportions[layer] = prop;
                zone.LLModifier[layer] = llModifer;
            }
        }

        private double DegToRad(double degs)
        {
            return degs * Math.PI / 180.0;
        }

        private double CalcRootAreaSemiEllipse(ZoneState zone, double rootAngle, double top, double bottom, double hDist)
        {
            if (zone.RootFront == 0.0 || zone.RootFront <= top)
            {
                return 0.0;
            }

            double meanDepth, layerThick, rootLength, sowDepth, layerArea, a;

            sowDepth = zone.plant.SowingData.Depth;
            double bottomNew = Math.Min(bottom, zone.RootFront);
            double topNew = Math.Max(top, sowDepth);

            zone.RootSpread = zone.RootLength * Math.Tan(DegToRad(rootAngle));   // Semi minor axis

            meanDepth = Math.Max(0.5 * (bottomNew + topNew) - sowDepth, 1); // 1mm is added to assure germination occurs.
            layerThick = Math.Max(bottomNew - topNew, 1);
            rootLength = Math.Max(zone.RootLength, 1);

            a = Math.Pow(meanDepth - 0.5 * rootLength, 2) / Math.Pow(0.5 * rootLength, 2);
            double hDistNew = Math.Min(hDist, Math.Sqrt(MathUtilities.Bound(Math.Pow(zone.RootSpread, 2) * (1 - a), 0, 100000)));
            layerArea = layerThick * hDistNew;
            return layerArea;
        }

        /// <summary>Writes documentation for this function by adding to the list of documentation tags.</summary>
        /// <param name="tags">The list of tags to add to.</param>
        /// <param name="headingLevel">The level (e.g. H2) of the headings.</param>
        /// <param name="indent">The level of indentation 1, 2, 3 etc.</param>
        public void Document(List<AutoDocumentation.ITag> tags, int headingLevel, int indent)
        {
            if (IncludeInDocumentation)
            {
                // add a heading.
                tags.Add(new AutoDocumentation.Heading(Name, headingLevel));

                // add graph and table.
                //tags.Add(new AutoDocumentation.Paragraph("<i>" + Name + " is calculated as a function of daily min and max temperatures, these are weighted toward VPD at max temperature according to the specified MaximumVPDWeight factor.  A value equal to 1.0 means it will use VPD at max temperature, a value of 0.5 means average VPD.</i>", indent));
                //tags.Add(new AutoDocumentation.Paragraph("<i>MaximumVPDWeight = " + MaximumVPDWeight + "</i>", indent));

                // write memos.
                foreach (IModel memo in Apsim.Children(this, typeof(Memo)))
                    AutoDocumentation.DocumentModel(memo, tags, headingLevel + 1, indent);
            }
        }
    }
}
