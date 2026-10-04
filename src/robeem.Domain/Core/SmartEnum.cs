namespace robeem.System.Contract
{
    public static class SmartEnum
    {
        /// <summary>
        /// Bounded contexts. Each member name doubles as the database schema name of the
        /// entities that belong to it (<c>Schema = nameof(SmartEnum.Module.HR)</c>).
        /// </summary>
        public enum Module
        {
            HR,
            Attendance,
        }
    }
}
