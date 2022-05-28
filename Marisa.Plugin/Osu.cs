﻿using System.Net.WebSockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;
using Flurl.Http;
using log4net;
using Marisa.EntityFrameworkCore;
using Marisa.EntityFrameworkCore.Entity.Plugin.Osu;
using Marisa.Plugin.Shared.FSharp.Osu;
using Marisa.Plugin.Shared.Osu;
using Microsoft.EntityFrameworkCore;
using Websocket.Client;

namespace Marisa.Plugin;

[MarisaPluginCommand("osu!", "osu", "!", "！")]
public class Osu : MarisaPluginBase
{
    private readonly WebsocketClient _wsClient;
    private readonly ILog _logger;
    private readonly BufferBlock<(long Id, string Recv)> _recvQueue = new();

    public Osu(ILog logger)
    {
        _logger = logger;
        var factory = new Func<ClientWebSocket>(() =>
        {
            var client = new ClientWebSocket
            {
                Options =
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(5),
                }
            };
            client.Options.SetRequestHeader("X-Self-ID", "2096937554");
            client.Options.SetRequestHeader("X-Client-Role", "Universal");
            return client;
        });

        _wsClient                  = new WebsocketClient(new Uri("ws://botws.desu.life:65000"), factory);
        _wsClient.ReconnectTimeout = TimeSpan.MaxValue;
    }

    public override async Task BackgroundService()
    {
        var regex = new Regex(@"""user_id"":(\d*)");

        _wsClient.ReconnectionHappened.Subscribe(_ => { _logger.Warn("Reconnect to 猫猫"); });

        _wsClient.MessageReceived.Subscribe(next =>
        {
            var t  = next.Text;
            var id = regex.Match(t).Groups[1].Value;
            _recvQueue.Post((long.Parse(id), t));
        });

        await _wsClient.Start();

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        var s = BuildCommand("heartbeat", 114514);

        while (await timer.WaitForNextTickAsync())
        {
            _wsClient.Send(s);
        }
    }

    private async Task<string> GetReplyByUserId(long userId)
    {
        var regex    = new Regex(@"""message"":""(.*?)""");
        var recvList = new List<(long, string)>();
        var res      = "";

        while (await _recvQueue.OutputAvailableAsync())
        {
            var recv = await _recvQueue.ReceiveAsync();
            if (recv.Id == userId)
            {
                res = regex.Match(recv.Recv).Groups[1].Value;
                break;
            }

            recvList.Add(recv);
        }

        recvList.ForEach(recv => _recvQueue.Post(recv));

        return res;
    }

    private async Task ReplyMessageByCommand(Message message, string command)
    {
        var cmd = BuildCommand(command, message.Sender!.Id);
        _wsClient.Send(cmd);

        var reply = await GetReplyByUserId(message.Sender!.Id);

        if (string.IsNullOrEmpty(reply))
        {
            message.Reply("猫猫不理魔理沙！");
            return;
        }

        if (reply.StartsWith("[CQ:image"))
        {
            var regex = new Regex(@"\[CQ:image,file=base64://(.*?)\]");
            message.Reply(MessageDataImage.FromBase64(regex.Match(reply).Groups[1].Value));
        }
        else
        {
            message.Reply(reply.Replace("猫猫", "魔理沙问猫猫，她说她"));
        }
    }

    /// <summary>
    /// 模拟 go-cqhttp 的消息
    /// </summary>
    /// <param name="command">猫猫 bot 需要的命令</param>
    /// <param name="userId">qq</param>
    /// <returns>go-cqhttp 生成的消息（json字符串）</returns>
    private static string BuildCommand(string command, long userId)
    {
        return
            @$"{{""font"":0,""message"":""!{command}"",""message_id"":0,""message_type"":""private"",""post_type"":""message"",""self_id"":0,""sender"":{{""age"":0,""nickname"":"""",""sex"":"""",""user_id"":0}},""sub_type"":""friend"",""target_id"":0,""time"":0,""user_id"":{userId}}}";
    }

    private async Task RunCommand(Message message, string prefix, bool withBpRank=false)
    {
        var command = OsuCommandParser.parser(message.Command)?.Value;

        if (command == null)
        {
            message.Reply("错误的命令格式");
            return;
        }

        if (command.BpRank != null && !withBpRank)
        {
            message.Reply("错误的命令格式");
            return;
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            var db = new BotDbContext();
            var at = message.MessageChain?.Messages.FirstOrDefault(m => m.Type == MessageDataType.At) as MessageDataAt;
            var o = at == null
                ? db.OsuBinds.FirstOrDefault(o => o.UserId == message.Sender!.Id)
                : db.OsuBinds.FirstOrDefault(o => o.UserId == at.Target);

            if (o != null)
            {
                command = new OsuCommandParser.OsuCommand(
                    o.OsuUserName, command.BpRank, command.Mode ?? OsuApi.ModeList.IndexOf(o.GameMode));
            }
            else
            {
                if (at != null)
                    command = new OsuCommandParser.OsuCommand($"[CQ:at,qq={at.Target}]", command.BpRank, command.Mode);
            }
        }
        await ReplyMessageByCommand(message, $"{prefix} {command}");
    }


    [MarisaPluginCommand("bind")]
    private static async Task<MarisaPluginTaskState> Bind(Message message, BotDbContext dbContext)
    {
        var name   = message.Command.Trim();
        var sender = message.Sender!.Id;

        if (string.IsNullOrEmpty(name))
        {
            message.Reply("？");
            return MarisaPluginTaskState.CompletedTask;
        }

        try
        {
            var info = await OsuApi.GetUserInfoByName(name);

            if (dbContext.OsuBinds.Any(o => o.OsuUserId == info.Id))
            {
                message.Reply($"名为 '{info.Username}' 的 osu 用户已被绑定。。");
            }
            else if (dbContext.OsuBinds.Any(o => o.UserId == sender))
            {
                var bind = dbContext.OsuBinds.First(o => o.UserId == sender);

                bind.OsuUserId   = info.Id;
                bind.OsuUserName = info.Username;
                bind.GameMode    = "";
                await dbContext.SaveChangesAsync();
                message.Reply("好了");
            }
            else
            {
                await dbContext.OsuBinds.AddAsync(new OsuBind
                {
                    UserId      = sender,
                    GameMode    = "",
                    OsuUserId   = info.Id,
                    OsuUserName = info.Username
                });
                await dbContext.SaveChangesAsync();
                message.Reply("好了");
            }
        }
        catch (FlurlHttpException e) when (e.StatusCode == 404)
        {
            message.Reply("NotFound");
        }

        return MarisaPluginTaskState.CompletedTask;
    }

    [MarisaPluginCommand("setMode", "set mode", "mode")]
    private static async Task<MarisaPluginTaskState> SetMode(Message message, BotDbContext db)
    {
        var sender = message.Sender!.Id;
        var mode   = message.Command.Trim();

        if (!OsuApi.ModeList.Contains(mode))
        {
            message.Reply("可选的模式：" + string.Join(", ", OsuApi.ModeList));
            return MarisaPluginTaskState.CompletedTask;
        }

        if (!db.OsuBinds.Any(o => o.UserId == sender))
        {
            message.Reply("您是？");
            return MarisaPluginTaskState.CompletedTask;
        }

        var o = await db.OsuBinds.FirstAsync(u => u.UserId == sender);
        o.GameMode = mode;
        db.OsuBinds.Update(o);
        await db.SaveChangesAsync();
        message.Reply("好了");

        return MarisaPluginTaskState.CompletedTask;
    }

    [MarisaPluginCommand("info")]
    private async Task<MarisaPluginTaskState> Info(Message message)
    {
        await RunCommand(message, "info");
        return MarisaPluginTaskState.CompletedTask;
    }

    [MarisaPluginCommand("pr")]
    private async Task<MarisaPluginTaskState> RecentPass(Message message, BotDbContext db)
    {
        await RunCommand(message, "pr");
        return MarisaPluginTaskState.CompletedTask;
    }

    [MarisaPluginCommand("recent")]
    private async Task<MarisaPluginTaskState> Recent(Message message, BotDbContext db)
    {
        await RunCommand(message, "recent");
        return MarisaPluginTaskState.CompletedTask;
    }

    [MarisaPluginCommand("bp")]
    private async Task<MarisaPluginTaskState> BestPerformance(Message message, BotDbContext db)
    {
        await RunCommand(message, "bp", true);
        return MarisaPluginTaskState.CompletedTask;
    }

    [MarisaPluginCommand("todaybp")]
    private async Task<MarisaPluginTaskState> TodayBp(Message message, BotDbContext db)
    {
        await RunCommand(message, "todaybp");
        return MarisaPluginTaskState.CompletedTask;
    }

    [MarisaPluginCommand("score")]
    private async Task<MarisaPluginTaskState> Score(Message message)
    {
        await ReplyMessageByCommand(message, message.Command);
        return MarisaPluginTaskState.CompletedTask;
    }
}