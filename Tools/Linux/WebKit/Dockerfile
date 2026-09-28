FROM ubuntu:24.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential cmake ninja-build gperf ruby python3 perl unifdef pkg-config xz-utils gettext git ca-certificates \
    libgtk-3-dev libsoup-3.0-dev libglib2.0-dev libicu-dev libjpeg-dev libpng-dev libwebp-dev libwoff-dev \
    liblcms2-dev libxml2-dev libxslt1-dev libsqlite3-dev libsecret-1-dev libtasn1-6-dev libgcrypt20-dev \
    libenchant-2-dev libharfbuzz-dev libfreetype-dev libfontconfig-dev libepoxy-dev libgbm-dev libdrm-dev \
    libegl-dev libgles-dev libwayland-dev wayland-protocols libxt-dev libx11-dev libatspi2.0-dev \
    libsystemd-dev libssl-dev libavif-dev \
    libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev libgstreamer-plugins-bad1.0-dev \
    libevent-dev libasound2-dev libopus-dev libpulse-dev libvpx-dev nasm \
    && rm -rf /var/lib/apt/lists/*
COPY COMMIT fetch-source.sh build.sh /
ENTRYPOINT ["/build.sh"]
