namespace TSIC.Domain.Constants
{
    public static class RoleConstants
    {
        // Role IDs (GUIDs)
        public const string Anonymous = "CBF3F384-190F-4962-BF58-40B095628DC8";
        public const string ApiAuthorized = "114C0272-57CD-4308-B653-79A43C547B63";
        public const string ClubRep = "6A26171F-4D94-4928-94FA-2FEFD42C3C3E";
        public const string Director = "FF4D1C27-F6DA-4745-98CC-D7E8121A5D06";
        public const string Family = "E0A8A5C3-A36C-417F-8312-E7083F1AA5A0";
        public const string Guest = "E956616B-DF48-4225-8A10-424229105711";
        public const string Player = "DAC0C570-94AA-4A88-8D73-6034F1F72F3A";
        public const string Recruiter = "0882FD56-3B98-4FC7-8A73-8E257B64E558";
        public const string RefAssignor = "122075A3-2C42-4092-97F1-9673DF5B6A2C";
        public const string Referee = "261D0E0D-3827-4B89-B4F1-ACE4BE4242A0";
        public const string Scorer = "C1572CED-B719-487D-BC07-81FB1904A369";
        public const string Staff = "1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA";
        public const string StoreAdmin = "5B9B7055-4530-4E46-B403-1019FD8B8418";
        public const string SuperDirector = "7B9EB503-53C9-44FA-94A0-17760C512440";
        public const string StpAdmin = "CE2CB370-5880-4624-A43E-048379C64331";
        public const string Superuser = "CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9";

        // Role Names (for claims and authorization policies)
        public static class Names
        {
            public const string SuperuserName = "Superuser";
            public const string DirectorName = "Director";
            public const string SuperDirectorName = "SuperDirector";
            public const string RefAssignorName = "Ref Assignor";
            public const string StoreAdminName = "Store Admin";
            public const string StaffName = "Staff";
            public const string FamilyName = "Family";
            public const string PlayerName = "Player";
            public const string UnassignedAdultName = "Unassigned Adult";
            public const string ClubRepName = "Club Rep";
            public const string StpAdminName = "STPAdmin";
        }
    }
}