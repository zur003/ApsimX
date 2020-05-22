﻿namespace UnitTests
{
    using APSIM.Shared.Utilities;
    using Models;
    using Models.Core;
    using Models.Interfaces;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;

    [TestFixture]
    class FertiliserTests
    {
        /// <summary>Test setup routine. Returns a soil properties that can be used for testing.</summary>
        [Serializable]
        public class MockSoil : Model, ISoil
        {
            public double[] Thickness { get; set; }

            public double[] NO3 { get; set; }
        }

        class MockSoilSolute : Model, ISolute
        {
            public MockSoilSolute(string name = "NO3")
            {
                Name = name;
            }
            public double[] kgha
            {
                get
                {
                    return (Parent as MockSoil).NO3;
                }
                set
                {
                    (Parent as MockSoil).NO3 = value;
                }
            }

            public double[] ppm => throw new NotImplementedException();

            public void SetKgHa(SoluteSetterType callingModelType, double[] value)
            {
                kgha = value;
            }

            public void AddKgHaDelta(SoluteSetterType callingModelType, double[] delta)
            {
                kgha = MathUtilities.Add(kgha, delta);
            }
        }


        /// <summary>Ensure the the apply method works with non zero depth.</summary>
        [Test]
        public void Fertiliser_EnsureApplyWorks()
        {
            // Create a tree with a root node for our models.
            var simulation = new Simulation()
            {
                Children = new List<IModel>()
                {
                    new Clock()
                    {
                        StartDate = new DateTime(2015, 1, 1),
                        EndDate = new DateTime(2015, 1, 1)
                    },
                    new MockSummary(),
                    new MockSoil()
                    {
                        Thickness = new double[] { 100, 100, 100 },
                        NO3 = new double[] { 1, 2, 3 },
                        Children = new List<IModel>()
                        {
                            new MockSoilSolute("NO3"),
                            new MockSoilSolute("NH4"),
                            new MockSoilSolute("Urea")
                        }
                    },
                    new Fertiliser() { Name = "Fertilise" },
                    new Operations()
                    {
                        Operation = new List<Operation>()
                        {
                            new Operation()
                            {
                                Date = "1-jan",
                                Action = "[Fertilise].Apply(Amount: 100, Type:Fertiliser.Types.NO3N, Depth:300)"
                            }
                        }
                    }
                }
            };

            simulation.Run();

            var soil = simulation.Children[2] as MockSoil;
            Assert.AreEqual(soil.NO3, new double[] { 1, 2, 103 });
            Assert.AreEqual(MockSummary.messages[0], "100 kg/ha of NO3N added at depth 300 layer 3");
        }



    }
}
