//
//  DefaultCultureProvider.cs
//
//  Copyright (c) Christofel authors. All rights reserved.
//  Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.Extensions.Options;

namespace Christofel.Helpers.Localization;

/// <summary>
/// Returns the default localization from the config.
/// </summary>
public class DefaultCultureProvider : ICultureProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultCultureProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public DefaultCultureProvider(IOptionsSnapshot<LocalizationOptions> options)
    {
        CurrentCulture = options.Value.DefaultLanguage;
    }

    /// <inheritdoc/>
    public string CurrentCulture { get; private set; }
}
