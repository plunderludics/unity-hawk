
function main()
    print("running main");
    while true do
        -- print("running a frame");
        emu.yield() -- frameadvance() also works
        -- console.clear();
        -- print("test lua");
        -- client.togglepause();

        -- unityhawktest.sayhello();

        -- pressed = joypad.getimmediate();
        -- print(pressed);
        -- joypad.set({['P1 C Up'] = true}) -- hold down the first-person look button
    end
end

main()