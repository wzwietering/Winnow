using Winnow.Internal.Validation;

namespace Winnow;

/// <summary>
/// Fluent helpers for attaching a pre-validation pipeline to a flat
/// (non-graph) options object: <see cref="WinnowOptions"/>,
/// <see cref="InsertOptions"/>, <see cref="DeleteOptions"/>, or
/// <see cref="UpsertOptions"/>.
/// </summary>
/// <remarks>
/// Overloads are provided per concrete options type so the fluent chain keeps
/// the caller's exact derived type — <c>new InsertOptions().WithDataAnnotations&lt;Order&gt;()</c>
/// still returns <see cref="InsertOptions"/>, not the base. All methods accept
/// an optional <see cref="ValidationFailureBehavior"/> so the entire
/// configuration is reachable in one call without a follow-up property
/// assignment.
/// </remarks>
public static class ValidationOptionsExtensions
{
    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, WinnowValidator{TEntity}, ValidationFailureBehavior)"/>
    public static WinnowOptions WithValidation<TEntity>(
        this WinnowOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlat(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, WinnowValidator{TEntity}, ValidationFailureBehavior)"/>
    public static InsertOptions WithValidation<TEntity>(
        this InsertOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlat(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, WinnowValidator{TEntity}, ValidationFailureBehavior)"/>
    public static DeleteOptions WithValidation<TEntity>(
        this DeleteOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlat(options, validator, onFailure);
        return options;
    }

    /// <summary>
    /// Attaches a delegate-driven pre-validation pipeline. The delegate is
    /// invoked once per entity in each batch; emit <see cref="ValidationError"/>
    /// instances via the collector to reject the entity. Calling this method
    /// more than once replaces the previously configured validator — the last
    /// call wins.
    /// </summary>
    /// <param name="options">The options receiver.</param>
    /// <param name="validator">The validator delegate.</param>
    /// <param name="onFailure">
    /// What to do when at least one entity fails validation. Default:
    /// <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </param>
    public static UpsertOptions WithValidation<TEntity>(
        this UpsertOptions options,
        WinnowValidator<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlat(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertOptions, ValidationFailureBehavior)"/>
    public static WinnowOptions WithDataAnnotations<TEntity>(
        this WinnowOptions options,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertOptions, ValidationFailureBehavior)"/>
    public static InsertOptions WithDataAnnotations<TEntity>(
        this InsertOptions options,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertOptions, ValidationFailureBehavior)"/>
    public static DeleteOptions WithDataAnnotations<TEntity>(
        this DeleteOptions options,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options, onFailure);
        return options;
    }

    /// <summary>
    /// Attaches a pre-validation pipeline driven by
    /// <c>System.ComponentModel.DataAnnotations</c> attributes declared on
    /// <typeparamref name="TEntity"/>'s public instance properties. Reflection
    /// cost is paid once per type and amortised across all subsequent batches.
    /// </summary>
    /// <param name="options">The options receiver.</param>
    /// <param name="onFailure">
    /// What to do when at least one entity fails validation. Default:
    /// <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </param>
    public static UpsertOptions WithDataAnnotations<TEntity>(
        this UpsertOptions options,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlatDataAnnotations<TEntity>(options, onFailure);
        return options;
    }

    private static void ConfigureFlat<TEntity>(
        WinnowOptions options, WinnowValidator<TEntity> validator, ValidationFailureBehavior onFailure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        var validation = ValidationOptions.CreateFlat(typeof(TEntity), validator);
        validation.FailureBehavior = onFailure;
        options.Validation = validation;
    }

    private static void ConfigureFlatDataAnnotations<TEntity>(
        WinnowOptions options, ValidationFailureBehavior onFailure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var validator = DataAnnotationsValidatorFactory.Create<TEntity>();
        var validation = ValidationOptions.CreateFlat(typeof(TEntity), validator, isDataAnnotationsValidator: true);
        validation.FailureBehavior = onFailure;
        options.Validation = validation;
    }
}
