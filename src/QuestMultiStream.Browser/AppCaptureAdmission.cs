namespace QuestMultiStream.Browser;

// Display feeds belong to the current app media intent. Component Start does
// not override an all-media Stop or start an independent stopped-app session.
internal static class AppCaptureAdmission {
    internal static void Require(OwnerLiveStatus current,OwnedSource source) {
        if(current.ReconciliationRequired||current.Phase is not ("starting" or "running")||
            !current.Selection.Members.Any(m=>m.DeviceId==source.DeviceId))
            throw new InvalidDataException("Current app media intent does not admit display Start");
    }
}
