namespace SocialMaster.Models;

public enum WorkerState
{
    Stopped,
    Idle,
    WaitingForLogin,
    Downloading,
    Uploading,
    Nurturing,
    Waiting,
    Error
}

public class WorkerStateChangedEventArgs : EventArgs
{
    public int AccountId { get; set; }
    public string Platform { get; set; } = "";
    public WorkerState State { get; set; }
    public string Message { get; set; } = "";
    public DateTime? NextActionTime { get; set; }
}
