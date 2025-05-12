dotnet tool uninstall -g Hona.CodeReviewAi
dotnet pack -c Release -o ./nupkg
dotnet tool install -g --prerelease --add-source "./nupkg/" Hona.CodeReviewAi