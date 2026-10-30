# Minimal LGPL FFmpeg for Ghostclip, cross-compiled for Windows x64.
#
# Only what Ghostclip uses is enabled: Desktop Duplication capture, the three
# hardware encoders, AAC, MPEG-TS/MP4/concat and the filters the recorder,
# save, trim and compress steps need. No --enable-gpl, no --enable-nonfree:
# every dependency is MIT-licensed headers or libraries.
#
#   docker build -t ghostclip-ffmpeg build/ffmpeg   (see build.ps1)

FROM ubuntu:24.04

ARG FFMPEG_VERSION=n8.1.3
# n12.2 headers run on NVIDIA drivers 551+; n13 would require 570+.
ARG NVCODEC_VERSION=n12.2.72.0
ARG AMF_VERSION=v1.5.2
ARG VPL_VERSION=v2.17.0

ENV DEBIAN_FRONTEND=noninteractive \
    PREFIX=/opt/deps \
    TRIPLE=x86_64-w64-mingw32 \
    PKG_CONFIG_LIBDIR=/opt/deps/lib/pkgconfig

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates git make cmake nasm pkg-config gcc libc6-dev \
        mingw-w64 g++-mingw-w64-x86-64-posix gcc-mingw-w64-x86-64-posix \
    && rm -rf /var/lib/apt/lists/* \
    # The posix thread model is what libvpl's C++ needs.
    && update-alternatives --set x86_64-w64-mingw32-gcc /usr/bin/x86_64-w64-mingw32-gcc-posix \
    && update-alternatives --set x86_64-w64-mingw32-g++ /usr/bin/x86_64-w64-mingw32-g++-posix

WORKDIR /src

# NVENC: header-only, loads nvEncodeAPI64.dll at runtime.
RUN git clone --depth 1 --branch ${NVCODEC_VERSION} https://github.com/FFmpeg/nv-codec-headers \
    && make -C nv-codec-headers PREFIX=${PREFIX} install

# AMF: header-only, loads amfrt64.dll at runtime.
RUN git clone --depth 1 --branch ${AMF_VERSION} --filter=blob:none --sparse https://github.com/GPUOpen-LibrariesAndSDKs/AMF \
    && cd AMF && git sparse-checkout set amf/public/include \
    && mkdir -p ${PREFIX}/include/AMF && cp -r amf/public/include/* ${PREFIX}/include/AMF/

# QuickSync: the oneVPL dispatcher, linked statically; it finds the Intel runtime installed with the driver.
RUN git clone --depth 1 --branch ${VPL_VERSION} https://github.com/intel/libvpl \
    && cmake -S libvpl -B libvpl/build \
        -DCMAKE_SYSTEM_NAME=Windows \
        -DCMAKE_C_COMPILER=${TRIPLE}-gcc -DCMAKE_CXX_COMPILER=${TRIPLE}-g++ -DCMAKE_RC_COMPILER=${TRIPLE}-windres \
        -DCMAKE_INSTALL_PREFIX=${PREFIX} -DCMAKE_BUILD_TYPE=Release \
        -DBUILD_SHARED_LIBS=OFF -DBUILD_TOOLS=OFF -DBUILD_EXAMPLES=OFF -DBUILD_TESTS=OFF -DINSTALL_EXAMPLES=OFF \
    && cmake --build libvpl/build -j"$(nproc)" \
    && cmake --install libvpl/build

RUN git clone --depth 1 --branch ${FFMPEG_VERSION} https://github.com/FFmpeg/FFmpeg ffmpeg

COPY configure-flags.txt /src/configure-flags.txt

RUN cd ffmpeg && ./configure \
        --prefix=/out --arch=x86_64 --target-os=mingw32 --cross-prefix=${TRIPLE}- \
        --pkg-config=pkg-config --pkg-config-flags=--static \
        --extra-cflags="-I${PREFIX}/include" \
        --extra-ldflags="-L${PREFIX}/lib -static" \
        --extra-libs="-lstdc++" \
        $(grep -v '^#' /src/configure-flags.txt | tr '\n' ' ') \
    && make -j"$(nproc)" \
    && make install \
    && ${TRIPLE}-strip /out/bin/*.exe \
    && cp COPYING.LGPLv2.1 /out/COPYING.LGPLv2.1
