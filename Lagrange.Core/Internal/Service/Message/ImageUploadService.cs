using Lagrange.Core.Common;
using Lagrange.Core.Internal.Event;
using Lagrange.Core.Internal.Event.Message;
using Lagrange.Core.Internal.Packets.Message.Element.Implementation;
using Lagrange.Core.Internal.Packets.Service.Oidb;
using Lagrange.Core.Internal.Packets.Service.Oidb.Common;
using Lagrange.Core.Utility;
using Lagrange.Core.Utility.Extension;
using ProtoBuf;
using FileInfo = Lagrange.Core.Internal.Packets.Service.Oidb.Common.FileInfo;

namespace Lagrange.Core.Internal.Service.Message;

[EventSubscribe(typeof(ImageUploadEvent))]
[Service("OidbSvcTrpcTcp.0x11c5_100")]
internal class ImageUploadService : BaseService<ImageUploadEvent>
{
    protected override bool Build(ImageUploadEvent input, BotKeystore keystore, BotAppInfo appInfo,
        BotDeviceInfo device, out Span<byte> output, out List<Memory<byte>>? extraPackets)
    {
        if (input.Entity.ImageStream is null) throw new Exception();

        string md5 = input.Entity.ImageStream.Value.Md5(true);
        string sha1 = input.Entity.ImageStream.Value.Sha1(true);

        // var buffer = new byte[1024]; // parse image header
        // int _ = input.Entity.ImageStream.Value.Read(buffer.AsSpan());
        var type = ImageResolver.Resolve(input.Entity.ImageStream.Value, out var size);
        string imageExt = type switch
        {
            ImageFormat.Jpeg => ".jpg",
            ImageFormat.Png => ".png",
            ImageFormat.Gif => ".gif",
            ImageFormat.Webp => ".webp",
            ImageFormat.Bmp => ".bmp",
            // ImageFormat.Tiff => ".tiff",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
        input.Entity.ImageStream.Value.Position = 0;

        string uid = (string.IsNullOrEmpty(input.TargetUid) ? keystore.Uid : input.TargetUid) ?? "";

        var packet = new OidbSvcTrpcTcpBase<NTV2RichMediaReq>(new NTV2RichMediaReq
        {
            ReqHead = new MultiMediaReqHead
            {
                Common = new CommonHead
                {
                    RequestId = 1,
                    Command = 100
                },
                Scene = new SceneInfo
                {
                    RequestType = 2,
                    BusinessType = 1,
                    SceneType = 1,
                    C2C = new C2CUserInfo
                    {
                        AccountType = 2,
                        TargetUid = uid
                    }
                },
                Client = new ClientMeta { AgentType = 2 },
            },
            Upload = new UploadReq
            {
                UploadInfo = new List<UploadInfo>
                {
                    new()
                    {
                        FileInfo = new FileInfo
                        {
                            FileSize = (uint)input.Entity.ImageStream.Value.Length,
                            FileHash = md5,
                            FileSha1 = sha1,
                            FileName = md5 + imageExt,
                            Type = new FileType
                            {
                                Type = 1,
                                PicFormat = (uint)type,
                                VideoFormat = 0,
                                VoiceFormat = 0
                            },
                            Width = (uint)size.X,
                            Height = (uint)size.Y,
                            Time = 0,
                            Original = 1
                        },
                        SubFileType = 0
                    }
                },
                TryFastUploadCompleted = true,
                SrvSendMsg = false,
                ClientRandomId = (ulong)Random.Shared.Next(),
                CompatQMsgSceneType = 1u,
                ExtBizInfo = new ExtBizInfo
                {
                    Pic = new PicExtBizInfo
                    {
                        BizType = (uint)input.Entity.SubType,
                        // This is very confusing, so we only implement the default summary for sub type 1
                        // and Tencent implements the others based on the default values.
                        TextSummary = input.Entity.Summary ?? (input.Entity.SubType == 1 ? "[\u52a8\u753b\u8868\u60c5]" : null!),
                        C2c = new PicExtBizInfoC2c
                        {
                            SubType = (uint)input.Entity.SubType
                        }
                    },
                    Video = new VideoExtBizInfo { BytesPbReserve = Array.Empty<byte>() },
                    Ptt = new PttExtBizInfo
                    {
                        BytesReserve = Array.Empty<byte>(),
                        BytesPbReserve = Array.Empty<byte>(),
                        BytesGeneralFlags = Array.Empty<byte>()
                    }
                },
                ClientSeq = 0,
                NoNeedCompatMsg = false
            }
        }, 0x11c5, 100, false, true);

        output = packet.Serialize();
        extraPackets = null;
        return true;
    }

    protected override bool Parse(Span<byte> input, BotKeystore keystore, BotAppInfo appInfo, BotDeviceInfo device,
        out ImageUploadEvent output, out List<ProtocolEvent>? extraEvents)
    {
        var packet = Serializer.Deserialize<OidbSvcTrpcTcpBase<NTV2RichMediaResp>>(input);
        var upload = packet.Body.Upload;
        var compat = Serializer.Deserialize<NotOnlineImage>(upload.CompatQMsg.AsSpan());

        output = ImageUploadEvent.Result((int)packet.ErrorCode, upload.MsgInfo, upload.UKey, upload.IPv4s, upload.SubFileInfos, compat);
        extraEvents = null;
        return true;
    }
}