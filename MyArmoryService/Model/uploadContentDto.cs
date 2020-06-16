namespace MyArmoryService.Model
{
    public class UploadContentDto
    {
        public string Id { get; set; }
        public string Permalink { get; set; }
        public string Identifier { get; set; }
        public int UploadTime { get; set; }
        public int EncounterTime { get; set; }
        public string Generator { get; set; }
        public string Language { get; set; }
        public string UserToken { get; set; }
        public string Error { get; set; }
        public UploadEncounterDto Encounter { get; set; }
        public UploadEvtcDto Evtc { get; set; }
        public UploadPlayerDto Players { get; set; }
    }
}
