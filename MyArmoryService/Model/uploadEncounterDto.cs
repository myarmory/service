﻿namespace MyArmoryService.Model
{
    public class UploadEncounterDto
    {
        public bool Success { get; set; }
        public int Duration { get; set; }
        public int CompDps { get; set; }
        public int NumberOfPlayers { get; set; }
        public int NumberOfGroups { get; set; }
        public int BossId { get; set; }
        public int Gw2Build { get; set; }
        public bool JsonAvailable { get; set; }
    }
}