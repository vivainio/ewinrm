import os, socket

def c(s):
    print ">",s
    os.system(s)

os.chdir("t1")
c("7z a ../t1.zip *")

os.chdir("../t2")
c("7z a ../t2.zip *")
os.chdir("..")

def connect(addr, port):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.connect((addr, port))
    return s

def send(addr, port, text):
    s = connect(addr, port)
    s.send(text)
    parts = []
    while 1:
        data = s.recv(1024)
        if not data:
            break
        parts.append(data)

    resp = "".join(parts)
    print resp
    return resp

PORT = 19800

def sendfile(fname):

    return send("localhost", PORT, open(fname, "rb").read())




sendfile("t1.zip")
sendfile("t2.zip")
sendfile("t3.cmd")
sendfile("t4.py")


