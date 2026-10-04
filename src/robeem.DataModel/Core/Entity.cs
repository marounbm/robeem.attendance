using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace robeem.DataModel
{
    [Serializable]
    public class Entity
    {
        [NotMapped]
        public object? this[string key]
        {
            get
            {
                return this.getPropertyValue(key);
            }
            set
            {
                if (this.getPropertyValue(key) as Entity == null)
                {
                    this.setPropertyValue(key, value);
                }
                else
                {
                    this.setPropertyValue($"{key}Id", value);
                }
            }
        }

        public Type EntityType => GetType();
        public virtual string PartitionName => EntityType.Name;


        [Key]
        [Column(Order = 0)]
        public int Id { get; set; }


        [Required]
        [MaxLength(255)]
        [Column(Order = 2)]
        public virtual string Title { get; set; } = "";


        public virtual DateTime CreatedOn { get; set; } = DateTime.Now;
        public virtual int CreatedBy { get; set; }
        public virtual DateTime ModifiedOn { get; set; } = DateTime.Now;
        public virtual int ModifiedBy { get; set; }
        public virtual Guid GUID { get; set; } = Guid.NewGuid();

        public bool isSystemEntity() => Set.isSystemEntity(EntityType);
        public bool isWorkflowEntity() => Set.isWorkflowEntity(EntityType);
    }
}
