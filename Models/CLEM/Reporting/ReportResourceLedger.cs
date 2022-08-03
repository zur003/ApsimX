using APSIM.Shared.Utilities;
using Models.CLEM.Resources;
using Models.Core;
using Models.Core.Attributes;
using Models.Core.Run;
using Models;
using Models.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using Models.CLEM.Interfaces;

namespace Models.CLEM.Reporting
{
    /// <summary>
    /// A report class for writing resource ledger output to the data store.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.ReportView")]
    [PresenterName("UserInterface.Presenters.CLEMReportResultsPresenter")]
    [ValidParent(ParentType = typeof(ZoneCLEM))]
    [ValidParent(ParentType = typeof(CLEMFolder))]
    [ValidParent(ParentType = typeof(Folder))]
    [Description("This report automatically generates a ledger of resource transactions")]
    [Version(1, 0, 4, "Report style property allows Type and Amount transaction reporting")]
    [Version(1, 0, 3, "Now includes Category and RelatesTo fields for grouping in analysis.")]
    [Version(1, 0, 2, "Updated to enable ResourceUnitsConverter to be used.")]
    [Version(1, 0, 1, "")]
    [HelpUri(@"Content/Features/Reporting/Ledgers.htm")]
    public class ReportResourceLedger : Models.Report, ICLEMUI
    {
        [Link]
        private ResourcesHolder resources = null;
        [Link]
        private ISummary summary = null;

        /// <summary>
        /// Gets or sets report groups for outputting
        /// </summary>
        [Description("Resource group")]
        [Category("General", "Resources")]
        [Core.Display(Type = DisplayType.DropDown, Values = "GetResourceGroupsAvailable")]
        [Required(AllowEmptyStrings = false, ErrorMessage = "A single Resource group must be provided for the Ledger Report")]
        public string ResourceGroupsToReport { get; set; }

        /// <summary>
        /// Style of transaction report to use
        /// </summary>
        [Description("Style of report")]
        [Category("General", "Resources")]
        public ReportTransactionStyle ReportStyle { get; set; }

        /// <summary>
        /// Report all losses as -ve values
        /// </summary>
        [Summary]
        [Description("Report losses as negative")]
        [Category("General", "Extras")]
        public bool ReportLossesAsNegative { get; set; }

        /// <summary>
        /// Include price conversion if available
        /// </summary>
        [Summary]
        [Description("Include resource pricing")]
        [Category("General", "Extras")]
        public bool IncludePrice { get; set; }

        /// <summary>
        /// Include financial year
        /// </summary>
        [Summary]
        [Description("Include financial year")]
        [Category("General", "Extras")]
        public bool IncludeFinancialYear { get; set; }

        /// <summary>
        /// Include unit conversion if available
        /// </summary>
        [Summary]
        [Description("Include all unit conversions")]
        [Category("General", "Extras")]
        public bool IncludeConversions { get; set; }

        /// <summary>
        /// Transaction category levels to include
        /// </summary>
        [Summary]
        [Description("Number of TransactionCategory levels to include as columns")]
        [Category("General", "Extras")]
        public int TransactionCategoryLevels { get; set; }

        /// <summary>
        /// Names of Transaction category levels columns
        /// </summary>
        [Summary]
        [Description("List of TransactionCategory level column names")]
        [Category("General", "Extras")]
        public string TransactionCategoryLevelColumnNames { get; set; }

        /// <summary>
        /// Custom variables to add
        /// </summary>
        [Summary]
        [Description("Custom variables")]
        [Category("General", "Extras")]
        [Core.Display(Type = DisplayType.MultiLineText)]
        public string[] CustomVariableNames { get; set; }

        /// <inheritdoc/>
        public string SelectedTab { get; set; }

        /// <summary>An event handler to allow us to initialize ourselves.</summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        [EventSubscribe("Commencing")]
        private void OnCommencing(object sender, EventArgs e)
        {
            int lossModifier = -1;
            if (ReportLossesAsNegative)
                lossModifier = 1;

            // check if running from a CLEM.Market
            bool market = (FindAncestor<Zone>().GetType() == typeof(Market));

            List<string> variableNames = new List<string>
            {
                "[Clock].Today as Date"
            };
            if(IncludeFinancialYear)
            {
                Finance financeStore = resources.FindResourceGroup<Finance>();
                if (financeStore != null)
                    variableNames.Add($"[Resources].{financeStore.Name}.FinancialYear as FY");
            }

            List<string> eventNames = new List<string>();

            if (ResourceGroupsToReport != null && ResourceGroupsToReport.Trim() != "")
            {
                // check it is a ResourceGroup
                CLEMModel model = resources.FindResource<ResourceBaseWithTransactions>(ResourceGroupsToReport);
                if (model == null)
                {
                    summary.WriteMessage(this, String.Format("Invalid resource group [{0}] in ReportResourceBalances [{1}]\r\nEntry has been ignored", this.ResourceGroupsToReport, this.Name), MessageType.Warning);
                }
                else
                {
                    bool pricingIncluded = false;
                    if (model.GetType() == typeof(RuminantHerd))
                    {
                        pricingIncluded = model.FindAllDescendants<AnimalPricing>().Where(a => a.Enabled).Count() > 0;

                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.ID as uID");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.Breed as Breed");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.Sex as Sex");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.Age as Age");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.Weight as Weight");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.AdultEquivalent as AE");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.SaleFlag as Category");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.Class as Class");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.HerdName as RelatesTo");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastIndividualChanged.PopulationChangeDirection as Change");

                        // ToDo: add pricing for ruminants including buy and sell pricing
                        // Needs update in CLEMResourceTypeBase and link between ResourcePricing and AnimalPricing.
                    }
                    else
                    {
                        pricingIncluded = model.FindAllDescendants<ResourcePricing>().Where(a => a.Enabled).Count() > 0;

                        if (ReportStyle == ReportTransactionStyle.GainAndLossColumns)
                        {
                            variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastTransaction.Gain as Gain");
                            variableNames.Add("[Resources]." + this.ResourceGroupsToReport + $".LastTransaction.Loss * {lossModifier} as Loss");
                        }
                        else
                        {
                            variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastTransaction.TransactionType as Type");
                            variableNames.Add("[Resources]." + this.ResourceGroupsToReport + $".LastTransaction.AmountModifiedForLoss({ReportLossesAsNegative}) as Amount");
                        }

                        // get all converters for this type of resource
                        if (IncludeConversions)
                        {
                            var converterList = model.FindAllDescendants<ResourceUnitsConverter>().Select(a => a.Name).Distinct();
                            if (converterList != null)
                            {
                                foreach (var item in converterList)
                                {
                                    if (ReportStyle == ReportTransactionStyle.GainAndLossColumns)
                                    {
                                        variableNames.Add($"[Resources].{this.ResourceGroupsToReport}.LastTransaction.ConvertTo(\"{item}\",\"gain\",{ReportLossesAsNegative}) as {item}_Gain");
                                        variableNames.Add($"[Resources].{this.ResourceGroupsToReport}.LastTransaction.ConvertTo(\"{item}\",\"loss\",{ReportLossesAsNegative}) as {item}_Loss");
                                    }
                                    else
                                    {
                                        variableNames.Add($"[Resources].{this.ResourceGroupsToReport}.LastTransaction.ConvertTo(\"{item}\", {ReportLossesAsNegative}) as {item}_Amount");
                                    }
                                }
                            } 
                        }

                        // add pricing
                        if (IncludePrice && pricingIncluded)
                        {
                            if (ReportStyle == ReportTransactionStyle.GainAndLossColumns)
                            {
                                variableNames.Add("[Resources]." + this.ResourceGroupsToReport + $".LastTransaction.ConvertTo(\"$gain\",\"gain\", {ReportLossesAsNegative}) as Price_Gain");
                                variableNames.Add("[Resources]." + this.ResourceGroupsToReport + $".LastTransaction.ConvertTo(\"$loss\",\"loss\", {ReportLossesAsNegative}) as Price_Loss"); 
                            }
                            else
                            {
                                variableNames.Add("[Resources]." + this.ResourceGroupsToReport + $".LastTransaction.ConvertTo(\"$gain\", {ReportLossesAsNegative}) as Price_Amount");
                            }
                        }

                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastTransaction.ResourceType.Name as Resource");
                        // if this is a multi CLEM model simulation then add a new column with the parent Zone name
                        if(FindAncestor<Simulation>().FindChild<Market>()!=null)
                        {
                            variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastTransaction.Activity.CLEMParentName as Source");
                        }
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastTransaction.Activity.Name as Activity");
                        variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastTransaction.RelatesToResource as RelatesTo");
                        if(TransactionCategoryLevels == 0)
                            variableNames.Add("[Resources]." + this.ResourceGroupsToReport + ".LastTransaction.Category as Category");
                        else
                        {
                            for (int i = 1; i <= TransactionCategoryLevels; i++)
                            {
                                string colname = $"Category{i}";
                                string[] parts = TransactionCategoryLevelColumnNames.Split(',').Distinct().ToArray();
                                if (parts.Length >= i && parts[i - 1].Trim() != "")
                                    colname = parts[i - 1].Replace(" ","");
                                variableNames.Add("[Resources]." + this.ResourceGroupsToReport + $".LastTransaction.CategoryByLevel({i}) as {colname}");
                            }
                        }
                    }
                }
                eventNames.Add("[Resources]." + this.ResourceGroupsToReport + ".TransactionOccurred");
            }

            if(CustomVariableNames != null)
                variableNames.AddRange(CustomVariableNames);

            VariableNames = variableNames.ToArray();
            EventNames = eventNames.ToArray();
            SubscribeToEvents();
        }

        /// <summary>
        /// return a list of resource group cpmponents available
        /// </summary>
        /// <returns>A list of names of components</returns>
        public IEnumerable<string> GetResourceGroupsAvailable()
        {
            List<string> results = new List<string>();
            Zone zone = this.FindAncestor<Zone>();
            if (!(zone is null))
            {
                ResourcesHolder resources = zone.FindChild<ResourcesHolder>();
                if (!(resources is null))
                {
                    foreach (var model in resources.FindAllChildren<ResourceBaseWithTransactions>())
                    {
                        results.Add(model.Name);
                    }
                }
            }
            return results.AsEnumerable();
        }

    }
}
