namespace Infrastructure.Email;

internal static class EmailTemplates
{
    private static string AppBaseUrl => Environment.GetEnvironmentVariable("APP_BASE_URL") ?? "https://app.doezy.com.br";

    private static string BaseLayout(string title, string preview, string contentHtml)
    {
        return $$"""
<!doctype html>
<html lang="pt-BR">
<head>
  <meta name="viewport" content="width=device-width,initial-scale=1"/>
  <meta name="x-apple-disable-message-reformatting"/>
  <title>{{Escape(title)}}</title>
  <style>
    body{margin:0;padding:0;background:#f6f7f9;font-family:Arial,Helvetica,sans-serif;color:#111}
    a{color:#111;text-decoration:none}
    .container{max-width:600px;margin:0 auto;padding:24px}
    .card{background:#fff;border-radius:12px;padding:24px;border:1px solid #eceff1}
    .btn{display:inline-block;padding:12px 16px;border-radius:8px;font-weight:bold;border:1px solid #111}
    .muted{color:#555}
    .footer{font-size:12px;color:#666;margin-top:12px}
    .brand{font-weight:800;font-size:18px}
    .sp{height:16px}
    .divider{height:1px;background:#eceff1;margin:16px 0}
    .preheader{display:none!important;opacity:0;color:transparent;height:0;width:0}
  </style>
</head>
<body>
  <div class="preheader">{{Escape(preview)}}</div>
  <div class="container">
    <div style="margin:8px 0 16px" class="brand">Doezy</div>
    <div class="card">{{contentHtml}}</div>
    <div class="footer">
      &copy; {{DateTime.UtcNow.Year}} Doezy — Este é um e-mail transacional.
      Gerencie suas notificações em {{AppBaseUrl}}/configuracoes/notificacoes
    </div>
  </div>
</body>
</html>
""";
    }

    public static (string Subject, string Html, string Text) NewLeadProfessional(
        string professionalName, string clientName, string serviceName, string leadUrl, string? city = null)
    {
        var content = $"""
<h1>Novo lead!</h1>
<p><b>{Escape(clientName)}</b> solicitou <b>{Escape(serviceName)}</b>.</p>
{(city != null ? $"<p class=\"muted\">Cidade: {Escape(city)}.</p>" : "")}
<div class="sp"></div>
<a class="btn" href="{leadUrl}">Abrir lead</a>
<div class="divider"></div>
<p class="muted">Se você não reconhece, ignore este e-mail.</p>
""";
        var html = BaseLayout("Novo lead recebido", $"Novo lead para {serviceName}", content);
        return ("Novo lead recebido", html, StripHtml(content));
    }

    public static (string Subject, string Html, string Text) ChatNewMessage(
        string recipientName, string senderName, string messageSnippet, string chatUrl)
    {
        var content = $"""
<h1>Nova mensagem</h1>
<p><b>{Escape(senderName)}</b> enviou uma mensagem:</p>
<blockquote class="muted">{Escape(Truncate(messageSnippet, 200))}</blockquote>
<div class="sp"></div>
<a class="btn" href="{chatUrl}">Abrir conversa</a>
""";
        var html = BaseLayout("Você recebeu uma nova mensagem", $"Nova mensagem de {senderName}", content);
        return ("Você recebeu uma nova mensagem", html, StripHtml(content));
    }

    public static (string Subject, string Html, string Text) BookingConfirmedProfessional(
        string professionalName, string clientName, string serviceName, string when, string bookingUrl, string? address = null)
    {
        var content = $"""
<h1>Agendamento confirmado</h1>
<p>Serviço: <b>{Escape(serviceName)}</b></p>
<p>Cliente: <b>{Escape(clientName)}</b></p>
<p>Quando: <b>{Escape(when)}</b></p>
{(address != null ? $"<p>Endereço: {Escape(address)}</p>" : "")}
<div class="sp"></div>
<a class="btn" href="{bookingUrl}">Ver detalhes</a>
""";
        var html = BaseLayout("Agendamento confirmado", $"Agendamento confirmado com {clientName}", content);
        return ("Agendamento confirmado", html, StripHtml(content));
    }

    public static (string Subject, string Html, string Text) BookingConfirmedClient(
        string clientName, string professionalName, string serviceName, string when, string bookingUrl, string? address = null)
    {
        var content = $"""
<h1>Agendamento confirmado</h1>
<p>Profissional: <b>{Escape(professionalName)}</b></p>
<p>Serviço: <b>{Escape(serviceName)}</b></p>
<p>Quando: <b>{Escape(when)}</b></p>
{(address != null ? $"<p>Endereço: {Escape(address)}</p>" : "")}
<div class="sp"></div>
<a class="btn" href="{bookingUrl}">Ver detalhes</a>
""";
        var html = BaseLayout("Seu agendamento foi confirmado", $"Seu agendamento com {professionalName} foi confirmado", content);
        return ("Seu agendamento foi confirmado", html, StripHtml(content));
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");

    private static string StripHtml(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", " ")
        .Replace("  ", " ").Trim();

    private static string Truncate(string s, int max) => s.Length > max ? s[..(max - 1)] + "…" : s;
}
