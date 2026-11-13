# Đổi số này cho khớp <TargetFramework> trong file .csproj (net8.0 -> 8.0, net9.0 -> 9.0, net10.0 -> 10.0)
ARG DOTNET_VERSION=8.0

# ---------- Giai đoạn 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY . .

# Tự tìm file giamsatbethuysan.csproj (dù nằm ở gốc repo hay trong thư mục con) rồi publish
RUN dotnet publish "$(find . -maxdepth 3 -name 'giamsatbethuysan.csproj' | head -n 1)" \
    -c Release -o /app/publish

# ---------- Giai đoạn 2: chạy ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

# Cổng thật do Render cấp qua biến PORT, Program.cs đã đọc biến này
EXPOSE 10000
ENTRYPOINT ["dotnet", "giamsatbethuysan.dll"]
