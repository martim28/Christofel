//
// SelfBan.cs
//
//   Copyright (c) Christofel authors. All rights reserved.
//   Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel.DataAnnotations.Schema;
using Remora.Rest.Core;

namespace Christofel.Management.Database.Models
{
    /// <summary>
    /// Represents table ResendRule holding data about channels to resend messages from to another channel.
    /// </summary>
    [Table("SelfBan", Schema = ManagementContext.SchemaName)]
    public class SelfBan
    {
        /// <summary>
        /// Gets or sets primary key of <see cref="SelfBanId" />.
        /// </summary>
        public int SelfBanId { get; set; }

        /// <summary>
        /// Gets or sets user who self banned them self.
        /// </summary>
        public Snowflake UserId { get; set; }

        /// <summary>
        /// Gets or sets when should the slowmode be deactivated.
        /// </summary>
        public DateTime DeactivationDate { get; set; }
    }
}