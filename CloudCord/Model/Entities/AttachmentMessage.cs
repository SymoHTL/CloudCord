namespace CloudCord.Model.Entities;

public class AttachmentMessage(IMessage dcMsg) {
    public ulong Id { get; set; } = dcMsg.Id;
    public string FileName { get; set; } = dcMsg.Attachments.First().Filename;
    public string Url { get; set; } = dcMsg.Attachments.First().Url;
}