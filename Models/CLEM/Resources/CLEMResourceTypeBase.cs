﻿using Models.Core;
using Models.Core.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Models.CLEM.Resources
{
    ///<summary>
    /// CLEM Resource Type base model
    ///</summary> 
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [Description("This is the CLEM Resource Type Base Class and should not be used directly.")]
    [Version(1, 0, 1, "")]
    public class CLEMResourceTypeBase : CLEMModel
    {
        [Link]
        Clock Clock = null;

        /// <summary>
        /// A link to the equivalent market store for trading.
        /// </summary>
        protected CLEMResourceTypeBase equivalentMarketStore { get; set; }

        /// <summary>
        /// Detemrines if an equivalent resource has been found in the market
        /// </summary>
        protected bool equivalentMarketStoreDetermined { get; set; }

        /// <summary>
        /// Determine whether transmutation has been defined for this foodtype
        /// </summary>
        [XmlIgnore]
        public bool TransmutationDefined 
        {
            get
            {
                return Apsim.Children(this, typeof(Transmutation)).Count() > 0;
            }
        }

        /// <summary>
        /// Does pricing exist for this tyep
        /// </summary>
        public bool PricingExists 
        {
            get
            {
                // find pricing that is ok;
                return Apsim.Children(this, typeof(ResourcePricing)).Where(a => (a as ResourcePricing).TimingOK).FirstOrDefault() != null;
            }
        }

        /// <summary>
        /// Resource price
        /// </summary>
        public ResourcePricing Price
        {
            get
            {
                // find pricing that is ok;
                ResourcePricing price = Apsim.Children(this, typeof(ResourcePricing)).Where(a => (a as ResourcePricing).TimingOK).FirstOrDefault() as ResourcePricing;

                var q = Apsim.Children(this, typeof(ResourcePricing));
                var r = q.Where(a => (a as ResourcePricing).TimingOK);

                if (price == null)
                {
                    string warn = "No pricing is available for [r=" + this.Parent.Name + "." + this.Name + "]";
                    if (Apsim.Children(this, typeof(ResourcePricing)).Count > 0)
                    {
                        warn += " in month [" + Clock.Today.ToString("MM yyyy") + "]";
                    }
                    warn += "\nAdd [r=ResourcePricing] component to [r=" + this.Parent.Name + "." + this.Name + "] to include financial transactions for purchases and sales.";

                    if (!Warnings.Exists(warn))
                    {
                        Summary.WriteWarning(this, warn);
                        Warnings.Add(warn);
                    }
                    return new ResourcePricing() { PricePerPacket=0, PacketSize=1, UseWholePackets=true };
                }
                return price;
            }
        }

        /// <summary>
        /// Convert specified amount of this resource to another value using ResourceType supplied converter
        /// </summary>
        /// <param name="converterName">Name of converter to use</param>
        /// <param name="amount">Amount to convert</param>
        /// <returns>Value to report</returns>
        public object ConvertTo(string converterName, double amount)
        {
            // get converted value
            if(converterName=="$")
            {
                // calculate price as special case using pricing structure if present.
                ResourcePricing price = Price;
                if(price.PricePerPacket > 0)
                {
                    double packets = amount / price.PacketSize;
                    // this does not include whole packet restriction as needs to report full value
                    return packets * price.PricePerPacket;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                ResourceUnitsConverter converter = Apsim.Children(this, typeof(ResourceUnitsConverter)).Where(a => string.Compare(a.Name, converterName, true) == 0).FirstOrDefault() as ResourceUnitsConverter;
                if (converter != null)
                {
                    double result = amount;
                    // convert to edible proportion for all HumanFoodStore converters
                    // this assumes these are all nutritional. Price will be handled above.
                    if(this.GetType() == typeof(HumanFoodStoreType))
                    {
                        result *= (this as HumanFoodStoreType).EdibleProportion;
                    }
                    return result * converter.Factor;
                }
                else
                {
                    string warning = "Unable to find the required unit converter [r=" + converterName + "] in resource [r=" + this.Name + "]";
                    Warnings.Add(warning);
                    Summary.WriteWarning(this, warning);
                    return null;
                }
            }
        }

        /// <summary>
        /// Convert the current amount of this resource to another value using ResourceType supplied converter
        /// </summary>
        /// <param name="converterName">Name of converter to use</param>
        /// <returns>Value to report</returns>
        public object ConvertTo(string converterName)
        {
            return ConvertTo(converterName, (this as IResourceType).Amount);
        }

        /// <summary>
        /// Convert the current amount of this resource to another value using ResourceType supplied converter
        /// </summary>
        /// <param name="converterName">Name of converter to use</param>
        /// <returns>Value to report</returns>
        public double ConversionFactor(string converterName)
        {
            ResourceUnitsConverter converter = Apsim.Children(this, typeof(ResourceUnitsConverter)).Where(a => a.Name.ToLower() == converterName.ToLower()).FirstOrDefault() as ResourceUnitsConverter;
            if (converter is null)
            {
                return 0;
            }
            else
            {
                return converter.Factor;
            }
        }

        /// <summary>
        /// Locate the equivalent store in a market if available
        /// </summary>
        protected void FindEquivalentMarketStore()
        {
            // determine what resource types allow market transactions
            switch (this.GetType().Name)
            {
                case "FinanceType":
                case "HumanFoodStoreType":
                case "WaterType":
                case "AnimalFoodType":
                case "EquipmentType":
                case "GreenhousGasesType":
                case "ProductStoreType":
                    break;
                default:
                    throw new NotImplementedException($"\n[r={this.Parent.GetType().Name}] resource does not currently support transactions to and from a [m=Market]\nThis problem has arisen because a resource transaction in the code is flagged to exchange resources with the [m=Market]\nPlease contact developers for assistance.");
            }

            // if not already checked
            if(!equivalentMarketStoreDetermined)
            {
                // havent already found a market store
                if(equivalentMarketStore is null)
                {
                    // is there a market
                    Market market = FindMarket();
                    if(market != null)
                    {
                        // get the resources
                        ResourcesHolder holder = Apsim.Child(market, typeof(ResourcesHolder)) as ResourcesHolder;
                        if(holder != null)
                        {
                            object store = null;
                            holder.ResourceTypeExists(this, out store);
                            if (store != null)
                            {
                                equivalentMarketStore = store as CLEMResourceTypeBase;
                            }
                        }

                    }
                }
            }
        }

        /// <summary>
        /// Add resources from various objects
        /// </summary>
        /// <param name="resourceAmount"></param>
        /// <param name="activity"></param>
        /// <param name="reason"></param>
        public void Add(object resourceAmount, CLEMModel activity, string reason)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Remove amount based on a ResourceRequest object
        /// </summary>
        /// <param name="request"></param>
        public void Remove(ResourceRequest request)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Set the amount of the resource. Use with caution as resources should be changed by add and remove methods.
        /// </summary>
        /// <param name="newAmount"></param>
        public void Set(double newAmount)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Clone this resource type
        /// </summary>
        public object Clone { get { throw new NotImplementedException(); } }

        /// <summary>
        /// Provides the description of the model settings for summary (GetFullSummary)
        /// </summary>
        /// <param name="formatForParentControl">Use full verbose description</param>
        /// <returns></returns>
        public override string ModelSummary(bool formatForParentControl)
        {
            string html = "";
            return html;
        }

    }
}
