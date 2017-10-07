#!/usr/bin/env bash

#echo on
set -x

docker build -t platform-level-techempower -f Dockerfile .
