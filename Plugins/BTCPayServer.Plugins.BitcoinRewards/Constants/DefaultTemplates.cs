#nullable enable

namespace BTCPayServer.Plugins.BitcoinRewards.Constants;

public static class DefaultTemplates
{
    public const string WaitingTemplate = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{STORE_NAME} Rewards</title>
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=Playfair+Display:wght@600;700&display=swap"" rel=""stylesheet"">
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body {
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: linear-gradient(135deg, {PRIMARY_COLOR} 0%, {SECONDARY_COLOR} 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }
        
        .waiting-container {
            background: #FFFEF7;
            border-radius: 24px;
            padding: 60px 40px;
            max-width: 500px;
            width: 100%;
            text-align: center;
            box-shadow: 0 20px 60px rgba(0,0,0,0.4);
            animation: pulse 2s ease-in-out infinite;
        }
        
        @keyframes pulse {
            0%, 100% { transform: scale(1); }
            50% { transform: scale(1.02); }
        }
        
        h1 {
            font-family: 'Playfair Display', Georgia, serif;
            color: {PRIMARY_COLOR};
            font-size: 36px;
            margin-bottom: 20px;
        }
        
        .hourglass {
            font-size: 64px;
            margin: 30px 0;
            animation: rotate 3s linear infinite;
        }
        
        @keyframes rotate {
            0% { transform: rotate(0deg); }
            100% { transform: rotate(180deg); }
        }
        
        .subtitle {
            color: #666;
            font-size: 18px;
            margin-bottom: 30px;
        }
        
        .info {
            background: {ACCENT_COLOR};
            padding: 20px;
            border-radius: 12px;
            margin-top: 30px;
            font-size: 14px;
            color: #4A4A4A;
        }
        
        .logo {
            max-width: 150px;
            margin-bottom: 20px;
        }
    </style>
    <script>
        setTimeout(() => location.reload(), {AUTO_REFRESH_MS});
    </script>
</head>
<body>
    <div class=""waiting-container"">
        {LOGO_HTML}
        <h1>{STORE_NAME} Rewards</h1>
        <div class=""subtitle"">Bitcoin Rewards Display</div>
        <div class=""hourglass"">⏳</div>
        <div class=""subtitle"">Waiting for rewards...</div>
        <div class=""info"">
            <strong>Auto-refresh:</strong> {AUTO_REFRESH_SECONDS} seconds<br>
            <strong>Timeframe:</strong> {TIMEFRAME_MINUTES} minutes<br>
            The latest unclaimed reward will appear here automatically
        </div>
    </div>
</body>
</html>";
}
