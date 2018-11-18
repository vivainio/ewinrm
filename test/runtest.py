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
    sock.sendall("$" + str(len(text)) + "\r\n" + text)

def send(addr, port, command, text):
    s = connect(addr, port)
    s.send("+%s\r\n" % command)
    send_bulk_string(s, text)
    print s.recv(2000)

PORT = 19802

def sendfile(command, fname):
    print "Running", command, fname
    cont = open(fname, "rb").read()
    print "Sending", len(cont)

    return send("localhost", PORT, command, cont)


#sendfile("run", "t4.py")
#sendfile("run", "t3.cmd")

#create_zips()
#sendfile("ziprun", "t1.zip")
sendfile("ziprun", "t2.zip")


