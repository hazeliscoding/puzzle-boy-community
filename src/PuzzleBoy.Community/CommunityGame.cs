using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace PuzzleBoy.Community;

/// <summary>The Community Edition window. It needs no game install: everything it shows comes from this repo.</summary>
internal sealed class CommunityGame : Game
{
    // The brand's night (ROADMAP.md, decisions).
    private static readonly Color Night = new(0x15, 0x10, 0x2A);

    /// <summary>Opens a 1280×720 window at the original's 30 ticks a second.</summary>
    public CommunityGame()
    {
        _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 1280, PreferredBackBufferHeight = 720 };
        Window.Title = "Puzzle Boy Community Edition";
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 30);
        IsMouseVisible = true;
    }

    /// <inheritdoc />
    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
        }

        base.Update(gameTime);
    }

    /// <inheritdoc />
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Night);
        base.Draw(gameTime);
    }
}
