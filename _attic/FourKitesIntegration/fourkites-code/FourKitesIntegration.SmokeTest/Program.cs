using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using FourKitesIntegration.Core.Client;
using FourKitesIntegration.Core.Mapping;
using FourKitesIntegration.Core.Models.Common;
using FourKitesIntegration.Core.Models.CreateShipment;
using FourKitesIntegration.Core.Models.DispatcherUpdate;

namespace FourKitesIntegration.SmokeTest
{
    /// <summary>
    /// Walks through the test sequence from Section 11 of the reference doc:
    ///   1. Connectivity check (empty body returns 400)
    ///   2. Create test shipment
    ///   3. Send a location update
    ///   4. Send an event update (pickup arrival)
    ///   5. Send a stop update (appointment reschedule)
    ///   6. Mark delivered
    /// Document upload + Get Document are NOT included here — they require a real PDF on disk.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                return RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex);
                return 99;
            }
        }

        private static async Task<int> RunAsync()
        {
            var apiKey = ConfigurationManager.AppSettings["FourKites.ApiKey"];
            if (string.IsNullOrEmpty(apiKey) || apiKey == "REPLACE_WITH_YOUR_STAGING_KEY")
            {
                Console.Error.WriteLine("ERROR: Set FourKites.ApiKey in App.config before running.");
                return 2;
            }

            var opts = new FourKitesClientOptions
            {
                Environment = "Staging",
                BaseHost = ConfigurationManager.AppSettings["FourKites.BaseHost"],
                ApiKey = apiKey,
                MaxRetryAttempts = 1, // Smoke test — don't hide errors with retries
                DefaultBillToCode = ConfigurationManager.AppSettings["SmokeTest.BillToCode"]
            };

            using (var client = new FourKitesClient(opts))
            {
                Console.WriteLine("=== FourKites Staging Smoke Test ===");
                Console.WriteLine("Host: " + opts.BaseHost);
                Console.WriteLine();

                // Generate a unique load number for this run.
                var loadNumber = ConfigurationManager.AppSettings["SmokeTest.LoadNumberPrefix"]
                    + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                Console.WriteLine("Test load number: " + loadNumber);
                Console.WriteLine();

                // Step 1: Connectivity check.
                Console.WriteLine("[1/6] Connectivity check (empty batch — expect 400 with requestId)...");
                var emptyBatch = new DispatcherBatch();
                var r1 = await client.SendDispatcherUpdateAsync(emptyBatch).ConfigureAwait(false);
                PrintResult(r1);
                if (r1.StatusCode != 400)
                    Console.WriteLine("  WARNING: Expected 400 but got " + r1.StatusCode);

                // Step 2: Create shipment.
                Console.WriteLine();
                Console.WriteLine("[2/6] Create test shipment...");
                var createReq = BuildTestShipment(loadNumber, opts.DefaultBillToCode);
                var r2 = await client.CreateShipmentAsync(createReq).ConfigureAwait(false);
                PrintResult(r2);
                if (!r2.IsSuccess)
                {
                    Console.WriteLine("  Aborting: Create Shipment failed.");
                    return 3;
                }

                // Step 3: Send a location update.
                Console.WriteLine();
                Console.WriteLine("[3/6] Location update (Chicago)...");
                var r3 = await client.SendDispatcherUpdateAsync(BuildLocationUpdate(loadNumber, opts.DefaultBillToCode))
                    .ConfigureAwait(false);
                PrintResult(r3);

                // Step 4: Event update — arrived at pickup.
                Console.WriteLine();
                Console.WriteLine("[4/6] Event update (X1 arrived at pickup)...");
                var r4 = await client.SendDispatcherUpdateAsync(BuildEventUpdate(loadNumber, opts.DefaultBillToCode))
                    .ConfigureAwait(false);
                PrintResult(r4);

                // Step 5: Stop update — reschedule the delivery.
                Console.WriteLine();
                Console.WriteLine("[5/6] Stop update (reschedule delivery appointment)...");
                var r5 = await client.SendDispatcherUpdateAsync(BuildStopReschedule(loadNumber, opts.DefaultBillToCode))
                    .ConfigureAwait(false);
                PrintResult(r5);

                // Step 6: Mark delivered.
                Console.WriteLine();
                Console.WriteLine("[6/6] Mark delivered (D1 + delivered=true)...");
                var r6 = await client.SendDispatcherUpdateAsync(BuildDeliveredEvent(loadNumber, opts.DefaultBillToCode))
                    .ConfigureAwait(false);
                PrintResult(r6);

                Console.WriteLine();
                Console.WriteLine("=== Smoke test complete ===");
                Console.WriteLine("Log into app.fourkites.com (staging) and find load " + loadNumber + " to verify.");
                Console.WriteLine("Rate limit remaining at end: " + client.RateLimitTracker.Remaining
                    + "/" + client.RateLimitTracker.Limit);
                return 0;
            }
        }

        private static CreateShipmentRequest BuildTestShipment(string loadNumber, string billToCode)
        {
            return new CreateShipmentRequest
            {
                AdditionalData = new AdditionalData
                {
                    ModeDetails = new ModeDetails { ShipperModes = "TL" }
                },
                Load = new LoadCreatePayload
                {
                    LoadNumber = loadNumber,
                    Carrier = ConfigurationManager.AppSettings["SmokeTest.Carrier"],
                    Priority = LoadPriority.Normal,
                    ReferenceNumbers = new List<string> { "SMOKE-REF-" + loadNumber },
                    Stops = new List<Stop>
                    {
                        new Stop
                        {
                            StopType = StopTypes.Pickup,
                            Sequence = 1,
                            StopReferenceId = "PU-1",
                            Name = "Smoke Test Pickup",
                            AddressLine1 = "500 W Madison St",
                            City = "Chicago",
                            State = "IL",
                            PostalCode = "60611",
                            Country = "US",
                            EarliestAppointmentTime = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss"),
                            LatestAppointmentTime = DateTime.UtcNow.AddDays(1).AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss")
                        },
                        new Stop
                        {
                            StopType = StopTypes.Delivery,
                            Sequence = 2,
                            StopReferenceId = "DEL-1",
                            Name = "Smoke Test Delivery",
                            AddressLine1 = "1001 E 17th St",
                            City = "Bloomington",
                            State = "IN",
                            PostalCode = "47408",
                            Country = "US",
                            EarliestAppointmentTime = DateTime.UtcNow.AddDays(1).AddHours(8).ToString("yyyy-MM-ddTHH:mm:ss"),
                            LatestAppointmentTime = DateTime.UtcNow.AddDays(1).AddHours(10).ToString("yyyy-MM-ddTHH:mm:ss")
                        }
                    },
                    TrackingInfo = new TrackingInfo
                    {
                        TruckNumber = "SMOKE-TR",
                        TrailerNumber = "SMOKE-TL"
                    }
                }
            };
        }

        private static DispatcherBatch BuildLocationUpdate(string loadNumber, string billToCode) =>
            new DispatcherBatch
            {
                Updates = new List<LoadUpdateEntry>
                {
                    new LoadUpdateEntry
                    {
                        BillToCode = billToCode,
                        IdentifierKeys = new List<IdentifierKey>
                        {
                            new IdentifierKey { Identifier = loadNumber, IdentifierType = IdentifierTypes.LoadNumber }
                        },
                        LoadUpdate = new List<LoadUpdatePayload>
                        {
                            new LoadUpdatePayload
                            {
                                LocationUpdate = new LocationUpdate
                                {
                                    Latitude = "41.881",
                                    Longitude = "-87.629",
                                    LocatedAt = FourKitesIntegration.Core.Models.Common.FourKitesTime.FormatUtc(DateTime.UtcNow),
                                    City = "Chicago",
                                    State = "IL"
                                }
                            }
                        }
                    }
                }
            };

        private static DispatcherBatch BuildEventUpdate(string loadNumber, string billToCode) =>
            new DispatcherBatch
            {
                Updates = new List<LoadUpdateEntry>
                {
                    new LoadUpdateEntry
                    {
                        BillToCode = billToCode,
                        IdentifierKeys = new List<IdentifierKey>
                        {
                            new IdentifierKey { Identifier = loadNumber, IdentifierType = IdentifierTypes.LoadNumber }
                        },
                        LoadUpdate = new List<LoadUpdatePayload>
                        {
                            new LoadUpdatePayload
                            {
                                EventUpdate = new EventUpdate
                                {
                                    StatusCode = Edi214Mapper.StatusCodes.ArrivedPickup,
                                    StatusDescription = Edi214Mapper.DescribeStatus(Edi214Mapper.StatusCodes.ArrivedPickup),
                                    StatusReasonCode = Edi214Mapper.ReasonCodes.Normal,
                                    EventTimeStamp = FourKitesIntegration.Core.Models.Common.FourKitesTime.FormatUtc(DateTime.UtcNow)
                                }
                            }
                        }
                    }
                }
            };

        private static DispatcherBatch BuildStopReschedule(string loadNumber, string billToCode) =>
            new DispatcherBatch
            {
                Updates = new List<LoadUpdateEntry>
                {
                    new LoadUpdateEntry
                    {
                        BillToCode = billToCode,
                        TimeZone = "America/Chicago",
                        IdentifierKeys = new List<IdentifierKey>
                        {
                            new IdentifierKey { Identifier = loadNumber, IdentifierType = IdentifierTypes.LoadNumber }
                        },
                        LoadUpdate = new List<LoadUpdatePayload>
                        {
                            new LoadUpdatePayload
                            {
                                StopUpdate = new StopUpdate
                                {
                                    StopReferenceId = "DEL-1",
                                    AppointmentTime = FourKitesIntegration.Core.Models.Common.FourKitesTime.FormatUtc(DateTime.UtcNow.AddDays(1).AddHours(12)),
                                    EarliestAppointmentTime = FourKitesIntegration.Core.Models.Common.FourKitesTime.FormatUtc(DateTime.UtcNow.AddDays(1).AddHours(11)),
                                    LatestAppointmentTime = FourKitesIntegration.Core.Models.Common.FourKitesTime.FormatUtc(DateTime.UtcNow.AddDays(1).AddHours(13))
                                }
                            }
                        }
                    }
                }
            };

        private static DispatcherBatch BuildDeliveredEvent(string loadNumber, string billToCode) =>
            new DispatcherBatch
            {
                Updates = new List<LoadUpdateEntry>
                {
                    new LoadUpdateEntry
                    {
                        BillToCode = billToCode,
                        IdentifierKeys = new List<IdentifierKey>
                        {
                            new IdentifierKey { Identifier = loadNumber, IdentifierType = IdentifierTypes.LoadNumber }
                        },
                        LoadUpdate = new List<LoadUpdatePayload>
                        {
                            new LoadUpdatePayload
                            {
                                EventUpdate = new EventUpdate
                                {
                                    StatusCode = Edi214Mapper.StatusCodes.LoadCompleted,
                                    StatusDescription = "Completed Unloading at Delivery",
                                    StatusReasonCode = Edi214Mapper.ReasonCodes.Normal,
                                    Delivered = true,
                                    DeliveredAt = FourKitesIntegration.Core.Models.Common.FourKitesTime.FormatUtc(DateTime.UtcNow),
                                    EventTimeStamp = FourKitesIntegration.Core.Models.Common.FourKitesTime.FormatUtc(DateTime.UtcNow)
                                }
                            }
                        }
                    }
                }
            };

        private static void PrintResult(FourKitesResponse r)
        {
            Console.WriteLine($"  Status:        {r.StatusCode}  ({r.ErrorClass})");
            Console.WriteLine($"  RequestId:     {r.RequestId ?? "(none)"}");
            Console.WriteLine($"  RateLimit:     {r.RateLimitRemaining}/{r.RateLimitLimit}");
            if (!string.IsNullOrEmpty(r.ErrorMessage))
                Console.WriteLine($"  Message:       {r.ErrorMessage}");
            if (!string.IsNullOrEmpty(r.TransportException))
                Console.WriteLine($"  Transport Err: {r.TransportException.Substring(0, Math.Min(200, r.TransportException.Length))}");
            if (!string.IsNullOrEmpty(r.Body))
            {
                var bodyPreview = r.Body.Length > 400 ? r.Body.Substring(0, 400) + "..." : r.Body;
                Console.WriteLine($"  Body:          {bodyPreview}");
            }
        }
    }
}
