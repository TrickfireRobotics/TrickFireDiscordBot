# TrickFireDiscordBot

This is the bot we use to track when our members come in and out of our lab.

Its primary purpose is so people know when members of respective teams are in
the shop and actively working. It also doubles as a way for members to see that
work is actually being done on the robot, which we think can increase a sense of
community and motivation.

# Contributing

This project is built using [DSharpPlus](https://dsharpplus.github.io/). To
build locally, simply open the solution file using your IDE of choice, then
build. Before running, ensure that there is a `secrets.txt` file in the build
directory with the Discord token of your bot on the first line. Additionally,
create a `config.json` file in the build directory with the properties defined
in the [config file](TrickFireDiscordBot/Config.cs).