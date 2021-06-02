# KotchatBot — How to use
First step — check appsettings.json for configuration. Seconds step — run the exe.

# KotchatBot — Configuration
Settings be found in appsettings.json.
### GeneralOptions
General settings for the bot, normally you need to change only BotName, all other params are predefined
### FolderDataSourceOptions
Takes files from specific folder
* Path: path to folder
* IncludeSubdirectories: if true will look for files in subdirectories

Bot command will be '.random', does not support arguments.
### ImgurDataSourceOptions
Takes files from Imgur, saving them on local disk
* ClientId: client id from Imgur
 
Bot command will be '.imgur'. Supports arguments.  Some examples of commands:
* .imgur — will return random images for the current day
* .imgur coffee shop — will return random images for 'coffee shop' query