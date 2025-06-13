//
//  20250613142500_AddGraduationProgrammeRole.cs
//
//  Copyright (c) Christofel authors. All rights reserved.
//  Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Christofel.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddGraduationProgrammeRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GraduationAssignmentId",
                schema: "Core",
                table: "ProgrammeRoleAssignment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgrammeRoleAssignment_GraduationAssignmentId",
                schema: "Core",
                table: "ProgrammeRoleAssignment",
                column: "GraduationAssignmentId");

            // Default to the AssignmentId
            migrationBuilder.Sql(
                @"UPDATE `Core_ProgrammeRoleAssignment` SET `GraduationAssignmentId` = `AssignmentId`"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_ProgrammeRoleAssignment_RoleAssignment_GraduationAssignmentId",
                schema: "Core",
                table: "ProgrammeRoleAssignment",
                column: "GraduationAssignmentId",
                principalSchema: "Core",
                principalTable: "RoleAssignment",
                principalColumn: "RoleAssignmentId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProgrammeRoleAssignment_RoleAssignment_GraduationAssignmentId",
                schema: "Core",
                table: "ProgrammeRoleAssignment");

            migrationBuilder.DropIndex(
                name: "IX_ProgrammeRoleAssignment_GraduationAssignmentId",
                schema: "Core",
                table: "ProgrammeRoleAssignment");

            migrationBuilder.DropColumn(
                name: "GraduationAssignmentId",
                schema: "Core",
                table: "ProgrammeRoleAssignment");
        }
    }
}
