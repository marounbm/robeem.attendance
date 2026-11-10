using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>
    /// A day on which the whole tenant does not work. One row per day: a holiday of three days is three
    /// rows. The name is the inherited <see cref="Entity.Title"/>.
    /// </summary>
    [Serializable]
    [Table(nameof(PublicHoliday), Schema = nameof(SmartEnum.Module.HR))]
    public class PublicHoliday : Entity, ITenantScoped, ISeedData
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Gets or sets the date (a date, without a time).</summary>
        public DateTime Date { get; set; }

        /// <summary>Gets or sets a value indicating whether the date follows the moon and may still move; shown as "to be confirmed".</summary>
        public bool IsProvisional { get; set; }
    }
}
