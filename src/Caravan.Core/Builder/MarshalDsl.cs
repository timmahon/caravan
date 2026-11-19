using Caravan.Model;
using Caravan.Spi;

namespace Caravan.Builder;

/// <summary>DSL for the <c>marshal</c> and <c>unmarshal</c> EIPs.</summary>
public static class MarshalDsl
{
    /// <summary>
    /// Marshals the body with <paramref name="dataFormat"/> (the body becomes the written bytes), e.g. <c>.Marshal(DataFormats.Json())</c>.
    /// </summary>
    /// <typeparam name="T">The block type.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="dataFormat">The data format.</param>
    /// <returns>The block.</returns>
    public static T Marshal<T>(this T steps, IDataFormat dataFormat)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(dataFormat);
        steps.AddStep(new MarshalDefinition { DataFormat = dataFormat });
        return steps;
    }

    /// <summary>
    /// Marshals the body with the data format named <paramref name="dataFormatName"/> (resolved when the route is created), e.g.
    /// <c>.Marshal("json")</c>.
    /// </summary>
    /// <typeparam name="T">The block type.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="dataFormatName">The data format name (see <see cref="ICaravanContext.ResolveDataFormat"/>).</param>
    /// <returns>The block.</returns>
    public static T Marshal<T>(this T steps, string dataFormatName)
        where T : RouteSteps
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFormatName);
        steps.AddStep(new MarshalDefinition { DataFormatName = dataFormatName });
        return steps;
    }

    /// <summary>Unmarshals the body with <paramref name="dataFormat"/>, e.g. <c>.Unmarshal(DataFormats.Json&lt;Order&gt;())</c>.</summary>
    /// <typeparam name="T">The block type.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="dataFormat">The data format.</param>
    /// <param name="allowNullBody">Leave a null body as is instead of failing.</param>
    /// <returns>The block.</returns>
    public static T Unmarshal<T>(this T steps, IDataFormat dataFormat, bool allowNullBody = false)
        where T : RouteSteps
    {
        ArgumentNullException.ThrowIfNull(dataFormat);
        steps.AddStep(new UnmarshalDefinition { DataFormat = dataFormat, AllowNullBody = allowNullBody });
        return steps;
    }

    /// <summary>Unmarshals the body with the data format named <paramref name="dataFormatName"/>, e.g. <c>.Unmarshal("csv")</c>.</summary>
    /// <typeparam name="T">The block type.</typeparam>
    /// <param name="steps">The block to add to.</param>
    /// <param name="dataFormatName">The data format name (see <see cref="ICaravanContext.ResolveDataFormat"/>).</param>
    /// <param name="allowNullBody">Leave a null body as is instead of failing.</param>
    /// <returns>The block.</returns>
    public static T Unmarshal<T>(this T steps, string dataFormatName, bool allowNullBody = false)
        where T : RouteSteps
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFormatName);
        steps.AddStep(new UnmarshalDefinition { DataFormatName = dataFormatName, AllowNullBody = allowNullBody });
        return steps;
    }
}
