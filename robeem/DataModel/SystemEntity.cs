using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace robeem.DataModel
{
    [Serializable]
    public class SystemEntity : Entity
    {
        [NotMapped]
        public override DateTime CreatedOn => DateTime.Now;
        [NotMapped]
        public override int CreatedBy => 0;
        [NotMapped]
        public override DateTime ModifiedOn => DateTime.Now;
        [NotMapped]
        public override int ModifiedBy => 0;
        [NotMapped]
        public override Guid GUID => Guid.Empty;
    }
}
