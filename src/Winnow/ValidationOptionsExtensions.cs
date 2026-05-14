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
    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, ValidatorDelegate{TEntity}, ValidationFailureBehavior)"/>
    public static WinnowOptions WithValidation<TEntity>(
        this WinnowOptions options,
        ValidatorDelegate<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlat(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, ValidatorDelegate{TEntity}, ValidationFailureBehavior)"/>
    public static InsertOptions WithValidation<TEntity>(
        this InsertOptions options,
        ValidatorDelegate<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureFlat(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertOptions, ValidatorDelegate{TEntity}, ValidationFailureBehavior)"/>
    public static DeleteOptions WithValidation<TEntity>(
        this DeleteOptions options,
        ValidatorDelegate<TEntity> validator,
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
        ValidatorDelegate<TEntity> validator,
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
        WinnowOptions options, ValidatorDelegate<TEntity> validator, ValidationFailureBehavior onFailure)
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

/// <summary>
/// Fluent helpers for attaching a pre-validation pipeline to a graph options
/// object: <see cref="InsertGraphOptions"/>, <see cref="GraphOptions"/>,
/// <see cref="DeleteGraphOptions"/>, or <see cref="UpsertGraphOptions"/>.
/// </summary>
/// <remarks>
/// Overloads are provided per concrete options type so the fluent chain keeps
/// the caller's exact derived type. The resulting
/// <see cref="GraphValidationOptions"/> exposes
/// <see cref="GraphValidationOptions.IncludeNavigations"/> for walking children;
/// the <c>WithDataAnnotations</c> overloads expose <c>includeNavigations</c> as
/// a parameter so the safe combination is reachable in one call.
/// </remarks>
public static class GraphValidationOptionsExtensions
{
    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, ValidatorDelegate{TEntity}, ValidationFailureBehavior)"/>
    public static InsertGraphOptions WithValidation<TEntity>(
        this InsertGraphOptions options,
        ValidatorDelegate<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, ValidatorDelegate{TEntity}, ValidationFailureBehavior)"/>
    public static GraphOptions WithValidation<TEntity>(
        this GraphOptions options,
        ValidatorDelegate<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithValidation{TEntity}(InsertGraphOptions, ValidatorDelegate{TEntity}, ValidationFailureBehavior)"/>
    public static DeleteGraphOptions WithValidation<TEntity>(
        this DeleteGraphOptions options,
        ValidatorDelegate<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <summary>
    /// Attaches a delegate-driven pre-validation pipeline to a graph options
    /// object. The resulting <see cref="GraphValidationOptions"/> exposes
    /// <see cref="GraphValidationOptions.IncludeNavigations"/> for walking
    /// child entities — note that <c>IncludeNavigations</c> requires a
    /// DataAnnotations validator (use <c>WithDataAnnotations</c> instead) and
    /// will reject assignment otherwise.
    /// </summary>
    /// <param name="options">The options receiver.</param>
    /// <param name="validator">The validator delegate.</param>
    /// <param name="onFailure">
    /// What to do when at least one entity fails validation. Default:
    /// <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </param>
    public static UpsertGraphOptions WithValidation<TEntity>(
        this UpsertGraphOptions options,
        ValidatorDelegate<TEntity> validator,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraph(options, validator, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions, bool, ValidationFailureBehavior)"/>
    public static InsertGraphOptions WithDataAnnotations<TEntity>(
        this InsertGraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions, bool, ValidationFailureBehavior)"/>
    public static GraphOptions WithDataAnnotations<TEntity>(
        this GraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    /// <inheritdoc cref="WithDataAnnotations{TEntity}(InsertGraphOptions, bool, ValidationFailureBehavior)"/>
    public static DeleteGraphOptions WithDataAnnotations<TEntity>(
        this DeleteGraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    /// <summary>
    /// Attaches a DataAnnotations-driven pre-validation pipeline to a graph
    /// options object. Pass <paramref name="includeNavigations"/> to descend
    /// into navigation properties and validate reachable children in the same
    /// call.
    /// </summary>
    /// <param name="options">The options receiver.</param>
    /// <param name="includeNavigations">
    /// When <c>true</c>, the walker validates entities reachable through
    /// navigation properties up to the configured depth limit. Default: <c>false</c>.
    /// </param>
    /// <param name="onFailure">
    /// What to do when at least one entity fails validation. Default:
    /// <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </param>
    public static UpsertGraphOptions WithDataAnnotations<TEntity>(
        this UpsertGraphOptions options,
        bool includeNavigations = false,
        ValidationFailureBehavior onFailure = ValidationFailureBehavior.RecordAsFailure)
        where TEntity : class
    {
        ConfigureGraphDataAnnotations<TEntity>(options, includeNavigations, onFailure);
        return options;
    }

    private static void ConfigureGraph<TEntity>(
        GraphOptionsBase options, ValidatorDelegate<TEntity> validator, ValidationFailureBehavior onFailure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        var validation = new GraphValidationOptions(typeof(TEntity), validator);
        validation.FailureBehavior = onFailure;
        options.Validation = validation;
    }

    private static void ConfigureGraphDataAnnotations<TEntity>(
        GraphOptionsBase options, bool includeNavigations, ValidationFailureBehavior onFailure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        var validator = DataAnnotationsValidatorFactory.Create<TEntity>();
        var validation = new GraphValidationOptions(typeof(TEntity), validator, isDataAnnotationsValidator: true)
        {
            FailureBehavior = onFailure
        };
        if (includeNavigations) validation.IncludeNavigations = true;
        options.Validation = validation;
    }
}
