using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A person employed by the tenant. Other entities reference it by role, e.g.
    /// <c>EmployeeId</c> / <c>Employee</c> on <see cref="TimeScheduleEmployee"/>.
    /// </summary>
    [Serializable]
    [Table(nameof(Profile), Schema = nameof(SmartEnum.Module.HR))]
    public class Profile : Entity, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string EmployeeNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(100)]
        public string? Department { get; set; }

        [MaxLength(100)]
        public string? JobTitle { get; set; }

        [Required]
        public DateTime HireDate { get; set; } = DateTime.Today;

        public DateTime? TerminationDate { get; set; }

        public bool IsActive { get; set; } = true;

        #region not mapped
        [NotMapped]
        public string FullName => $"{FirstName} {LastName}".Trim();
        #endregion
    }
}
