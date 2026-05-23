using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Coverage added during the 1.3.0 release readiness pass:
/// ParallelWinnower + validation, IncludeNavigations e2e, Throw on graph/async,
/// mid-validation cancellation for Update/Delete/Upsert, and DataAnnotations
/// cache correctness across multiple types.
/// </summary>
public class ValidationReleaseReadinessTests
{
    // --- Task #7: ParallelWinnower + WithValidation -----------------------

    public class ParallelValidation : ParallelTestBase
    {
        [Fact]
        public async Task InsertAsync_WithValidation_RejectsInvalidAcrossPartitions()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m, // every third entity invalid
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions();
            options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
            {
                if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive");
            });

            var result = await saver.InsertAsync(products, options);

            var expectedInvalidIndices = Enumerable.Range(0, 8).Where(i => i % 3 == 0).ToList();
            result.FailureCount.ShouldBe(expectedInvalidIndices.Count);
            result.SuccessCount.ShouldBe(8 - expectedInvalidIndices.Count);
            result.Failures.Select(f => f.EntityIndex).OrderBy(x => x)
                .ShouldBe(expectedInvalidIndices);
            result.Failures.ShouldAllBe(f => f.Reason == FailureReason.PreValidationError);
        }

        [Fact]
        public async Task InsertAsync_ValidationFailure_PreservesValidationErrorsAcrossPartitions()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions();
            options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
            {
                if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
            });

            var result = await saver.InsertAsync(products, options);

            result.Failures.ShouldNotBeEmpty();
            foreach (var failure in result.Failures)
            {
                failure.ValidationErrors.ShouldNotBeNull();
                failure.ValidationErrors!.ShouldContain(e => e.Code == "RANGE");
            }
        }

        [Fact]
        public async Task UpsertAsync_ValidationFailure_PreservesValidationErrorsAcrossPartitions()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var factory = CreateContextFactory();
            var saver = new ParallelWinnower<Product, int>(factory, maxDegreeOfParallelism: 2);
            var options = new UpsertOptions();
            options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
            {
                if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
            });

            var result = await saver.UpsertAsync(products, options);

            var validationFailures = result.Failures.Where(f => f.Reason == FailureReason.PreValidationError).ToList();
            validationFailures.ShouldNotBeEmpty();
            foreach (var failure in validationFailures)
            {
                failure.ValidationErrors.ShouldNotBeNull();
                failure.ValidationErrors!.ShouldContain(e => e.Code == "RANGE");
            }
        }

        // Locks B4: in ParallelWinnower with ThrowAfterBatch, only the entities the
        // validator rejected become failures. Valid siblings in the same partition are
        // NOT swept into the failure bucket — the orchestrator catches the partition's
        // WinnowValidationException, removes the failed entities, and re-runs the
        // partition with the survivors so they reach the database.
        [Fact]
        public async Task InsertAsync_ThrowBehavior_RecordsOnlyOffendingEntitiesAndPersistsSurvivors()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();
            var expectedInvalidIndices = Enumerable.Range(0, 8).Where(i => i % 3 == 0).ToList();
            var expectedValidIndices = Enumerable.Range(0, 8).Where(i => i % 3 != 0).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions()
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.InsertAsync(products, options);

            result.FailureCount.ShouldBe(expectedInvalidIndices.Count);
            result.SuccessCount.ShouldBe(expectedValidIndices.Count);

            var failureIndices = result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ToList();
            failureIndices.ShouldBe(expectedInvalidIndices);
            foreach (var failure in result.Failures)
            {
                failure.Reason.ShouldBe(FailureReason.PreValidationError);
                failure.ValidationErrors.ShouldNotBeNull();
                failure.ValidationErrors!.ShouldContain(e => e.Code == "RANGE");
            }
        }

        // Regression: in parallel ThrowAfterBatch recovery at ResultDetail.None,
        // BuildInsertValidationFailures returned an empty list (detail-gated) and
        // the merger then computed FailureCount = survivor + 0, silently dropping
        // the count of validation-rejected entities. Locking the merger to use
        // the raw failure count rather than the gated list's length.
        [Fact]
        public async Task InsertAsync_ThrowBehavior_ResultDetailNone_FailureCountStillAccurate()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m, // invalid at 0, 3, 6 → 3 failures
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions { ResultDetail = ResultDetail.None }
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.InsertAsync(products, options);

            result.FailureCount.ShouldBe(3);
            result.SuccessCount.ShouldBe(5);
        }

        // Same regression as InsertAsync_…_FailureCountStillAccurate, but for
        // the WinnowResult (Update/Delete) merger path. MergeWinnow lives in
        // ValidationResultMerger; without the raw-count fix, FailureCount
        // collapses to zero at ResultDetail.None when validation rejects
        // entities via Throw recovery.
        [Fact]
        public async Task UpdateAsync_ThrowBehavior_ResultDetailNone_FailureCountStillAccurate()
        {
            EnsureDatabaseCreated();
            SeedWithFactory(ctx =>
            {
                ctx.Products.AddRange(Enumerable.Range(0, 8).Select(i => new Product
                {
                    Name = $"p{i}", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow,
                }));
                ctx.SaveChanges();
            });

            var products = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());
            foreach (var (p, i) in products.Select((p, i) => (p, i)))
            {
                p.Price = i % 3 == 0 ? -1m : 2m;
            }

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new WinnowOptions { ResultDetail = ResultDetail.None }
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.UpdateAsync(products, options);

            result.FailureCount.ShouldBe(3);
            result.SuccessCount.ShouldBe(5);
        }

        [Fact]
        public async Task DeleteAsync_ThrowBehavior_ResultDetailNone_FailureCountStillAccurate()
        {
            EnsureDatabaseCreated();
            SeedWithFactory(ctx =>
            {
                // Names prefixed "x_" mark entities pre-validation should reject; the
                // DbContext only validates Stock/Price so the seed itself is valid.
                ctx.Products.AddRange(Enumerable.Range(0, 8).Select(i => new Product
                {
                    Name = i % 3 == 0 ? $"x_{i}" : $"p{i}",
                    Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow,
                }));
                ctx.SaveChanges();
            });

            var products = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new DeleteOptions { ResultDetail = ResultDetail.None }
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Name.StartsWith("x_"))
                            c.Add(nameof(Product.Name), "Reserved prefix", "RESERVED");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.DeleteAsync(products, options);

            result.FailureCount.ShouldBe(3);
            result.SuccessCount.ShouldBe(5);
        }

        // Mirrors the Insert variant for the MergeUpsert path. Same fix
        // (raw-count vs gated-list count) lives in BuildUpsertValidationFailures.
        [Fact]
        public async Task UpsertAsync_ThrowBehavior_ResultDetailNone_FailureCountStillAccurate()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m, // invalid at 0, 3, 6 → 3 failures
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new UpsertOptions { ResultDetail = ResultDetail.None }
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.UpsertAsync(products, options);

            result.FailureCount.ShouldBe(3);
            result.SuccessCount.ShouldBe(5);
        }

        // Regression for the parallel ThrowAfterBatch recovery path: when a
        // partition's WinnowValidationException fires, the orchestrator re-runs
        // the survivors through the strategy, which assigns them fresh
        // 0..N-1 indices in the survivor list. Without the orchestrator-side
        // remap to partition-relative positions, InsertedEntity.OriginalIndex
        // pointed at the wrong source entity once ResultMerger added the
        // partition offset. The Product.Name carries its source index so we
        // can confirm each surviving InsertedEntity is paired with the entity
        // it actually came from.
        [Fact]
        public async Task InsertAsync_ThrowBehavior_InsertedEntities_RetainCorrectOriginalIndex()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m, // invalid at 0, 3, 6
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new InsertOptions()
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.InsertAsync(products, options);

            var expectedSurvivorIndices = Enumerable.Range(0, 8).Where(i => i % 3 != 0).ToList();
            result.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(x => x)
                .ShouldBe(expectedSurvivorIndices);

            foreach (var inserted in result.InsertedEntities)
            {
                var product = (Product)inserted.Entity;
                product.Name.ShouldBe($"p{inserted.OriginalIndex}");
                products[inserted.OriginalIndex].ShouldBeSameAs(product);
            }
        }

        // Same regression for the upsert recovery path. Upsert results carry
        // EntityIndex on UpsertFailure, OriginalIndex on UpsertedEntity, and
        // AttemptedOperation / IsDefaultKey on each upsert failure — all three
        // were corrupted before the fix (Index pointed at wrong entity;
        // AttemptedOperation was always Insert; IsDefaultKey was always false).
        [Fact]
        public async Task UpsertAsync_ThrowBehavior_AttributesUpsertResultsToCorrectEntity()
        {
            EnsureDatabaseCreated();

            var products = Enumerable.Range(0, 8).Select(i => new Product
            {
                Name = $"p{i}",
                Price = i % 3 == 0 ? -1m : 1m, // invalid at 0, 3, 6
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new UpsertOptions()
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.UpsertAsync(products, options);

            var expectedInvalid = new[] { 0, 3, 6 };
            var expectedValid = new[] { 1, 2, 4, 5, 7 };

            result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ShouldBe(expectedInvalid);
            result.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(x => x).ShouldBe(expectedValid);

            // Validation-failed upserts all had default keys (Id = 0), so they
            // were attempted as INSERTs — IsDefaultKey must be true.
            foreach (var failure in result.Failures)
            {
                failure.Reason.ShouldBe(FailureReason.PreValidationError);
                failure.AttemptedOperation.ShouldBe(UpsertOperationType.Insert);
                failure.IsDefaultKey.ShouldBeTrue();
            }

            foreach (var inserted in result.InsertedEntities)
            {
                var product = (Product)inserted.Entity;
                product.Name.ShouldBe($"p{inserted.OriginalIndex}");
            }
        }

        // Asymmetric variant: pre-seed some entities so the upsert mixes
        // INSERT (default key) and UPDATE (real key) candidates. After
        // validation rejects one of the existing-key entities, the failure
        // must report AttemptedOperation = Update, not Insert.
        [Fact]
        public async Task UpsertAsync_ThrowBehavior_ExistingKeyFailure_ReportsUpdateAttempt()
        {
            EnsureDatabaseCreated();
            SeedWithFactory(c =>
            {
                c.Products.Add(new Product
                {
                    Id = 9_999, Name = "existing", Price = 5m, Stock = 1,
                    LastModified = DateTimeOffset.UtcNow,
                });
                c.SaveChanges();
            });

            var products = new List<Product>
            {
                new() { Name = "new-ok-0", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                new() { Id = 9_999, Name = "existing-bad", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                new() { Name = "new-ok-2", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                new() { Name = "new-bad-3", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            };

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new UpsertOptions()
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.UpsertAsync(products, options);

            var failureByIndex = result.Failures.ToDictionary(f => f.EntityIndex);
            failureByIndex.ShouldContainKey(1);
            failureByIndex[1].AttemptedOperation.ShouldBe(UpsertOperationType.Update);
            failureByIndex[1].IsDefaultKey.ShouldBeFalse();

            failureByIndex.ShouldContainKey(3);
            failureByIndex[3].AttemptedOperation.ShouldBe(UpsertOperationType.Insert);
            failureByIndex[3].IsDefaultKey.ShouldBeTrue();
        }

        // Regression lock: parallel-Throw recovery must remap UpdatedEntities.OriginalIndex
        // back to user-visible input positions. Existing coverage exercises the insert side
        // (InsertedEntities remap) and the failure side (AttemptedOperation), but no test
        // asserts on the update-side OriginalIndex — so RemapUpsertSurvivor's update branch
        // is otherwise unverified.
        [Fact]
        public async Task UpsertAsync_ThrowBehavior_RemapsUpdatedEntityOriginalIndices()
        {
            EnsureDatabaseCreated();
            SeedWithFactory(c =>
            {
                c.Products.Add(new Product { Id = 9_998, Name = "seed-1", Price = 5m, Stock = 1, LastModified = DateTimeOffset.UtcNow });
                c.Products.Add(new Product { Id = 9_997, Name = "seed-2", Price = 5m, Stock = 1, LastModified = DateTimeOffset.UtcNow });
                c.SaveChanges();
            });

            // Re-read the seeded entities so we have their rowversion-equivalent Version
            // values — otherwise EF Core's optimistic concurrency check rejects the UPDATE.
            var seeded = QueryWithFactory(c => c.Products.AsNoTracking().Where(p => p.Id == 9_998 || p.Id == 9_997).ToList());
            var seed1 = seeded.Single(p => p.Id == 9_998);
            var seed2 = seeded.Single(p => p.Id == 9_997);
            seed1.Name = "updated-1"; seed1.Price = 7m;
            seed2.Name = "updated-3"; seed2.Price = 8m;

            // Mix valid INSERT, valid UPDATE, and validation-rejected entries so the
            // partition survives through both Throw-recovery (validator rejects index 2)
            // and the remap (survivors carry original-index translation back to user positions).
            var products = new List<Product>
            {
                new() { Name = "new-ok-0", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                seed1,
                new() { Name = "new-bad-2", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                seed2,
                new() { Name = "new-ok-4", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            };

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new UpsertOptions()
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector c) =>
                    {
                        if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.UpsertAsync(products, options);

            result.Failures.Select(f => f.EntityIndex).ShouldBe(new[] { 2 });
            result.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(x => x).ShouldBe(new[] { 0, 4 });
            result.UpdatedEntities.Select(e => e.OriginalIndex).OrderBy(x => x).ShouldBe(new[] { 1, 3 });

            // Sanity-check the entity payload itself was remapped, not just the index.
            foreach (var updated in result.UpdatedEntities)
            {
                var product = (Product)updated.Entity;
                product.Name.ShouldBe($"updated-{updated.OriginalIndex}");
            }
        }

        // Covers ValidationResultMerger.ClassifyUpsertFailure's null-entity guard
        // through the parallel Throw recovery path. The single-context path goes
        // through OperationPreValidationHelper.RecordUpsertFailure (separate code);
        // the parallel path reaches ClassifyUpsertFailure via BuildUpsertFailures
        // when reconstructing the failure objects, and a null partition slot must
        // classify cleanly as Insert + IsDefaultKey rather than NRE. Uses
        // maxDegreeOfParallelism=2 to exercise the orchestrator path (parallelism=1
        // bypasses it via ExecuteSequentialAsync). Partition split with chunkSize=3
        // places the null entity in partition 0, where the recovery runs.
        [Fact]
        public async Task UpsertAsync_ThrowBehavior_NullEntity_ReportsInsertAttemptAndIsDefaultKey()
        {
            EnsureDatabaseCreated();

            var products = new List<Product>
            {
                new() { Name = "valid-0", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                null!,
                new() { Name = "valid-2", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                new() { Name = "valid-3", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
                new() { Name = "valid-4", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            };

            var saver = CreateSaver(maxDegreeOfParallelism: 2);
            var options = new UpsertOptions()
                .WithValidation<Product>(
                    (Product p, ref ValidationCollector _) => { /* never rejects — null guard fires it */ },
                    ValidationFailureBehavior.Throw);

            var result = await saver.UpsertAsync(products, options);

            var failure = result.Failures.ShouldHaveSingleItem();
            failure.EntityIndex.ShouldBe(1);
            failure.Reason.ShouldBe(FailureReason.PreValidationError);
            failure.AttemptedOperation.ShouldBe(UpsertOperationType.Insert);
            failure.IsDefaultKey.ShouldBeTrue();
        }

        // Locks RemapInsertFailures: under parallel Throw recovery, when the
        // survivor re-run produces per-entity DB failures (here: unique-constraint
        // violation on OrderNumber), those failures must carry partition-relative
        // EntityIndex values — NOT the survivor-local 0..N-1 indices that the
        // strategy actually saw. Without the remap, a callback reading
        // failure.EntityIndex would point at the wrong source entity.
        // 6 entities × parallelism=2 → partition 0 spans original indices 0..2;
        // the validator rejects 0, the DB rejects 2 after the survivor re-run.
        [Fact]
        public async Task InsertAsync_ThrowBehavior_DbFailureOnSurvivor_RemapsEntityIndexCorrectly()
        {
            EnsureDatabaseCreated();
            SeedWithFactory(c =>
            {
                c.CustomerOrders.Add(new CustomerOrder
                {
                    OrderNumber = "DUP",
                    CustomerName = "seeded",
                    CustomerId = 1,
                    Status = CustomerOrderStatus.Pending,
                    TotalAmount = 10m,
                    OrderDate = DateTimeOffset.UtcNow,
                });
                c.SaveChanges();
            });

            // Partition 0 (indices 0..2):
            //   0 — empty OrderNumber: rejected by validator
            //   1 — "OK-1": valid + unique
            //   2 — "DUP": valid by validator, DB rejects (unique constraint)
            // Partition 1 (indices 3..5): all clean inserts
            var orders = new List<CustomerOrder>
            {
                new() { OrderNumber = "", CustomerName = "bad-0", CustomerId = 1, Status = CustomerOrderStatus.Pending, TotalAmount = 10m, OrderDate = DateTimeOffset.UtcNow },
                new() { OrderNumber = "OK-1", CustomerName = "good-1", CustomerId = 1, Status = CustomerOrderStatus.Pending, TotalAmount = 10m, OrderDate = DateTimeOffset.UtcNow },
                new() { OrderNumber = "DUP", CustomerName = "bad-2", CustomerId = 1, Status = CustomerOrderStatus.Pending, TotalAmount = 10m, OrderDate = DateTimeOffset.UtcNow },
                new() { OrderNumber = "OK-3", CustomerName = "good-3", CustomerId = 1, Status = CustomerOrderStatus.Pending, TotalAmount = 10m, OrderDate = DateTimeOffset.UtcNow },
                new() { OrderNumber = "OK-4", CustomerName = "good-4", CustomerId = 1, Status = CustomerOrderStatus.Pending, TotalAmount = 10m, OrderDate = DateTimeOffset.UtcNow },
                new() { OrderNumber = "OK-5", CustomerName = "good-5", CustomerId = 1, Status = CustomerOrderStatus.Pending, TotalAmount = 10m, OrderDate = DateTimeOffset.UtcNow },
            };

            var saver = new ParallelWinnower<CustomerOrder, int>(
                CreateContextFactory(), maxDegreeOfParallelism: 2);
            var options = new InsertOptions()
                .WithValidation<CustomerOrder>(
                    (CustomerOrder o, ref ValidationCollector c) =>
                    {
                        if (string.IsNullOrEmpty(o.OrderNumber))
                            c.Add(nameof(CustomerOrder.OrderNumber), "required", "REQUIRED");
                    },
                    ValidationFailureBehavior.Throw);

            var result = await saver.InsertAsync(orders, options);

            var validationFailure = result.Failures.SingleOrDefault(f => f.Reason == FailureReason.PreValidationError);
            validationFailure.ShouldNotBeNull();
            validationFailure!.EntityIndex.ShouldBe(0);

            var dbFailure = result.Failures.SingleOrDefault(f => f.Reason != FailureReason.PreValidationError);
            dbFailure.ShouldNotBeNull();
            // The critical assertion: EntityIndex points back at the *original* batch
            // position (2), not the survivor-local position (1) that the strategy saw.
            dbFailure!.EntityIndex.ShouldBe(2);
        }
    }

    // --- Task #8: IncludeNavigations end-to-end through InsertGraph -------

    public class IncludeNavigationsEndToEnd : TestBase
    {
        private static CustomerOrder Order(string number, params OrderItem[] items) => new()
        {
            OrderNumber = number,
            CustomerName = "C",
            CustomerId = 1,
            TotalAmount = 1m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = items.ToList(),
        };

        private static OrderItem Item(string? productName) => new()
        {
            ProductId = 1,
            ProductName = productName!,
            Quantity = 1,
            UnitPrice = 1m,
            Subtotal = 1m,
        };

        [Fact]
        public void InsertGraph_DataAnnotationsWithIncludeNavigations_RejectsParentForBadChild()
        {
            using var context = CreateContext();
            var orders = new[]
            {
                Order("OK", Item("Valid")),
                Order("BAD-CHILD", Item(null)),
            };

            var options = new InsertGraphOptions().WithDataAnnotations<CustomerOrder>();
            options.Validation!.IncludeNavigations = true;

            var saver = new Winnower<CustomerOrder, int>(context);
            var result = saver.InsertGraph(orders, options);

            result.SuccessCount.ShouldBe(1);
            result.FailureCount.ShouldBe(1);
            var failure = result.Failures.ShouldHaveSingleItem();
            failure.EntityIndex.ShouldBe(1);
            failure.Reason.ShouldBe(FailureReason.PreValidationError);
            failure.ValidationErrors.ShouldNotBeNull();
            failure.ValidationErrors!.ShouldContain(e => e.PropertyName.Contains("ProductName"));

            context.ChangeTracker.Clear();
            context.CustomerOrders.Count(o => o.OrderNumber == "BAD-CHILD").ShouldBe(0);
            context.CustomerOrders.Count(o => o.OrderNumber == "OK").ShouldBe(1);
        }
    }

    // --- Task #9: Throw mode on graph and async ---------------------------

    public class ThrowModeCoverage : TestBase
    {
        private static WinnowValidator<Product> AlwaysFail()
            => (Product _, ref ValidationCollector c) => c.Add("X", "always");

        private static WinnowValidator<CustomerOrder> RejectOrderNumber(string n)
            => (CustomerOrder o, ref ValidationCollector c) =>
            {
                if (o.OrderNumber == n) c.Add(nameof(CustomerOrder.OrderNumber), "Rejected");
            };

        [Fact]
        public void InsertGraph_ThrowBehavior_ThrowsWinnowValidationException()
        {
            using var context = CreateContext();
            var orders = new[]
            {
                new CustomerOrder
                {
                    OrderNumber = "BAD", CustomerName = "C", CustomerId = 1,
                    TotalAmount = 1m, OrderDate = DateTimeOffset.UtcNow,
                    OrderItems = [new OrderItem { ProductId = 1, ProductName = "P", Quantity = 1, UnitPrice = 1m, Subtotal = 1m }],
                }
            };

            var options = new InsertGraphOptions();
            options.WithValidation(RejectOrderNumber("BAD"));
            options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

            var saver = new Winnower<CustomerOrder, int>(context);
            var ex = Should.Throw<WinnowValidationException>(() => saver.InsertGraph(orders, options));
            ex.Failures.Count.ShouldBe(1);
            ex.Failures[0].EntityIndex.ShouldBe(0);
            ex.Failures[0].ValidationErrors.ShouldContain(e => e.PropertyName == nameof(CustomerOrder.OrderNumber));

            context.ChangeTracker.Clear();
            context.CustomerOrders.Count().ShouldBe(0);
        }

        [Fact]
        public async Task InsertAsync_ThrowBehavior_ThrowsWinnowValidationException()
        {
            using var context = CreateContext();
            var products = new[]
            {
                new Product { Name = "A", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            };

            var options = new InsertOptions();
            options.WithValidation(AlwaysFail());
            options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<WinnowValidationException>(
                () => saver.InsertAsync(products, options));
        }
    }

    // --- Task #10: Mid-validation cancellation for Update/Delete/Upsert ---

    public class MidValidationCancellation : TestBase
    {
        private static List<Product> Pad(int n) =>
            Enumerable.Range(0, n).Select(i => new Product
            {
                Id = i + 100_000,
                Name = $"p{i}",
                Price = 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            }).ToList();

        private static WinnowOptions Options(CancellationTokenSource cts, ref int counter)
        {
            // The validator counter must be captured by ref through a wrapper; use a
            // boxed holder so the lambda can mutate it.
            var holder = new Counter { Value = 0 };
            var options = new WinnowOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;
            counter = holder.Value;
            return options;
        }

        private sealed class Counter { public int Value; }

        [Fact]
        public async Task UpdateAsync_CancelledMidValidation_ThrowsOperationCanceled()
        {
            using var context = CreateContext();
            SeedData(context, 1);
            var products = await context.Products.AsNoTracking().ToListAsync();
            products.AddRange(Pad(500));

            using var cts = new CancellationTokenSource();
            var holder = new Counter();
            var options = new WinnowOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<OperationCanceledException>(
                () => saver.UpdateAsync(products, options, cts.Token));
        }

        [Fact]
        public async Task DeleteAsync_CancelledMidValidation_ThrowsOperationCanceled()
        {
            using var context = CreateContext();
            SeedData(context, 1);
            var products = await context.Products.AsNoTracking().ToListAsync();
            products.AddRange(Pad(500));

            using var cts = new CancellationTokenSource();
            var holder = new Counter();
            var options = new DeleteOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<OperationCanceledException>(
                () => saver.DeleteAsync(products, options, cts.Token));
        }

        [Fact]
        public async Task UpsertAsync_CancelledMidValidation_ThrowsOperationCanceled()
        {
            using var context = CreateContext();
            var products = Pad(500);

            using var cts = new CancellationTokenSource();
            var holder = new Counter();
            var options = new UpsertOptions();
            options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            {
                if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
            });
            options.Validation!.CancellationCheckInterval = 1;

            var saver = new Winnower<Product, int>(context);
            await Should.ThrowAsync<OperationCanceledException>(
                () => saver.UpsertAsync(products, options, cts.Token));
        }
    }

    // --- Task #11: DataAnnotations cache across multiple types ------------

    public class TwoTypeCache
    {
        public sealed class TypeA
        {
            [Required] public string? Name { get; set; }
        }

        public sealed class TypeB
        {
            [Range(1, 10)] public int Score { get; set; }
        }

        [Fact]
        public void WithDataAnnotations_ConfiguredForTwoTypes_EachReportsOwnPropertyName()
        {
            var aOptions = new InsertOptions().WithDataAnnotations<TypeA>();
            var bOptions = new InsertOptions().WithDataAnnotations<TypeB>();

            var aValidator = (WinnowValidator<TypeA>)aOptions.Validation!.Validator;
            var bValidator = (WinnowValidator<TypeB>)bOptions.Validation!.Validator;

            var aBuffer = new ValidationError[4];
            var aCollector = new ValidationCollector(aBuffer);
            aValidator(new TypeA { Name = null }, ref aCollector);

            var bBuffer = new ValidationError[4];
            var bCollector = new ValidationCollector(bBuffer);
            bValidator(new TypeB { Score = 0 }, ref bCollector);

            aCollector.AsSpan().ToArray()
                .ShouldContain(e => e.PropertyName == nameof(TypeA.Name));
            bCollector.AsSpan().ToArray()
                .ShouldContain(e => e.PropertyName == nameof(TypeB.Score));
        }
    }
}
