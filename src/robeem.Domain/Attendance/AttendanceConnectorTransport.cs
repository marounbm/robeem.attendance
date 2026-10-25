using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SmartEnum = robeem.System.Contract.SmartEnum;

namespace robeem.DataModel
{
    /// <summary>One transport an <see cref="AttendanceConnectorDefinition"/> supports.</summary>
    [Serializable]
    [Table(nameof(AttendanceConnectorTransport), Schema = nameof(SmartEnum.Module.Attendance))]
    public class AttendanceConnectorTransport : SystemEntity, ISeedData, ITenantScoped
    {
        [Required]
        [MaxLength(ITenantScoped.TenantIdMaxLength)]
        public string TenantId { get; set; } = string.Empty;

        [Required]
        public int ConnectorId { get; set; }

        public AttendanceConnectorDefinition? Connector { get; set; }

        [Required]
        public int TransportId { get; set; }

        public AttendanceTransportDefinition? Transport { get; set; }

        #region not mapped
        [NotMapped]
        public override string Title => PartitionName;
        #endregion
    }
}
