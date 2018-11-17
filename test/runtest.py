import os, socket


def c(s):
    print ">",s
    os.system(s)

def create_zips():
    os.chdir("t1")
    c("7z a ../t1.zip *")

    os.chdir("../t2")
    c("7z a ../t2.zip *")
    os.chdir("..")

def connect(addr, port):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.connect((addr, port))
    return s


def send_bulk_string(sock, text):
    sock.send("$" + str(len(text)) + "\r\n" + text)

def send(addr, port, command, text):
    s = connect(addr, port)
    s.send("+%s\r\n" % command)
    send_bulk_string(s, text)
    print s.recv(2000)

PORT = 19800

def sendfile(command, fname):
    print "Running", command, fname
    return send("localhost", PORT, command, open(fname, "rb").read())


sendfile("run", "t4.py")
sendfile("run", "t3.cmd")

create_zips()
sendfile("run", "t1.zip")
sendfile("run", "t2.zip")


