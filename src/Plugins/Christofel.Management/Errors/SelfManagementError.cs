//
//   SelfManagementError.cs
//
//   Copyright (c) Christofel authors. All rights reserved.
//   Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Remora.Results;

namespace Christofel.Management.Errors;

/// <summary>
/// An error for capturing generic errors about self management,
/// mostly for cases when the user cannot do the action, because
/// assumptions are invalidated. Ie. the user already has timeout, ban...
/// </summary>
public record SelfManagementError(string Where, string ErrorMessage)
    : ResultError($"{Where}: {ErrorMessage}");
